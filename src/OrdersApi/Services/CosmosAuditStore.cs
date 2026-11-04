using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using OrdersApi.Models;

namespace OrdersApi.Services;

/// <summary>
/// Persists audit events to Azure Cosmos DB. Falls back to a no-op when no
/// connection string is configured; Program.cs wires InMemoryAuditStore in that
/// case so local development and tests do not need a Cosmos emulator.
/// </summary>
public sealed class CosmosAuditStore : IAuditStore, IAsyncDisposable
{
    private readonly OpenBankingOptions _options;
    private readonly ILogger<CosmosAuditStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CosmosClient? _client;
    private Container? _container;

    public CosmosAuditStore(IOptions<OpenBankingOptions> options, ILogger<CosmosAuditStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CosmosConnectionString);

    public async Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }
        var container = await GetContainerAsync(cancellationToken);
        await container.UpsertItemAsync(auditEvent, new PartitionKey(auditEvent.Id), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Array.Empty<AuditEvent>();
        }
        var container = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition($"SELECT TOP {Math.Clamp(limit, 1, 500)} * FROM c ORDER BY c.occurredAt DESC");
        using var iterator = container.GetItemQueryIterator<AuditEvent>(query);
        var results = new List<AuditEvent>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }
        return results;
    }

    private async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (_container is not null)
        {
            return _container;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_container is not null)
            {
                return _container;
            }
            _client = new CosmosClient(_options.CosmosConnectionString);
            var database = await _client.CreateDatabaseIfNotExistsAsync(_options.CosmosDatabase, cancellationToken: cancellationToken);
            var container = await database.Database.CreateContainerIfNotExistsAsync(
                _options.CosmosContainer, "/id", cancellationToken: cancellationToken);
            _container = container.Container;
            _logger.LogInformation("Cosmos audit container ready: {Database}/{Container}", _options.CosmosDatabase, _options.CosmosContainer);
            return _container;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
