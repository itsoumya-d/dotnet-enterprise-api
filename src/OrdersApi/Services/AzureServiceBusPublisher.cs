using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using OrdersApi.Models;

namespace OrdersApi.Services;

/// <summary>
/// Publishes audit events to an Azure Service Bus queue. The publisher is a
/// no-op when no connection string is configured, which keeps local runs and
/// integration tests free of cloud credentials.
/// </summary>
public sealed class AzureServiceBusPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly OpenBankingOptions _options;
    private readonly ILogger<AzureServiceBusPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;

    public AzureServiceBusPublisher(IOptions<OpenBankingOptions> options, ILogger<AzureServiceBusPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.AzureServiceBusConnectionString);

    public async Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        await EnsureSenderAsync(cancellationToken);
        var body = JsonSerializer.Serialize(auditEvent);
        var message = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            Subject = auditEvent.Type,
            MessageId = auditEvent.Id,
        };
        await _sender!.SendMessageAsync(message, cancellationToken);
        _logger.LogInformation("Published {EventType} {EventId} to Azure Service Bus", auditEvent.Type, auditEvent.Id);
    }

    private async Task EnsureSenderAsync(CancellationToken cancellationToken)
    {
        if (_sender is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_sender is not null)
            {
                return;
            }
            _client ??= new ServiceBusClient(_options.AzureServiceBusConnectionString);
            _sender = _client.CreateSender(_options.AzureQueue);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
        _gate.Dispose();
    }
}
