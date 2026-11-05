using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;
using OrdersApi.Models;

namespace OrdersApi.Services;

public class IntegrationService(
    OrdersDbContext db,
    IIntegrationDispatcher dispatcher,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<TenantIntegration> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName) ||
            string.IsNullOrWhiteSpace(request.SourceSystem) ||
            string.IsNullOrWhiteSpace(request.TargetSystem))
        {
            throw new ArgumentException("tenantName, sourceSystem and targetSystem are required");
        }

        var tenant = new TenantIntegration
        {
            TenantName = request.TenantName.Trim(),
            SourceSystem = request.SourceSystem.Trim(),
            TargetSystem = request.TargetSystem.Trim(),
        };
        db.TenantIntegrations.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task<IntegrationEvent> EnqueueAsync(EnqueueIntegrationEventRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.EventType))
        {
            throw new ArgumentException("idempotencyKey and eventType are required");
        }

        var tenant = await db.TenantIntegrations.FindAsync([request.TenantIntegrationId], cancellationToken)
            ?? throw new KeyNotFoundException("tenant integration not found");

        var existing = await db.IntegrationEvents
            .SingleOrDefaultAsync(item => item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.EventType == request.EventType && existing.Payload == request.Payload)
            {
                return existing;
            }

            throw new InvalidOperationException("idempotency key already used with a different payload");
        }

        var integrationEvent = new IntegrationEvent
        {
            TenantIntegrationId = tenant.Id,
            EventType = request.EventType,
            IdempotencyKey = request.IdempotencyKey,
            Payload = request.Payload,
            NextAttemptAt = _time.GetUtcNow(),
        };
        db.IntegrationEvents.Add(integrationEvent);
        await db.SaveChangesAsync(cancellationToken);
        return integrationEvent;
    }

    public async Task<IntegrationEvent> ProcessAsync(int id, CancellationToken cancellationToken)
    {
        var integrationEvent = await db.IntegrationEvents.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException("integration event not found");

        if (integrationEvent.Status == "processed")
        {
            return integrationEvent;
        }

        integrationEvent.Attempts++;
        try
        {
            await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
            integrationEvent.Status = "processed";
            integrationEvent.LastError = null;
            integrationEvent.NextAttemptAt = _time.GetUtcNow();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            integrationEvent.LastError = ex.Message;
            integrationEvent.Status = integrationEvent.Attempts >= 3 ? "dead_letter" : "pending";
            integrationEvent.NextAttemptAt = _time.GetUtcNow().AddSeconds(Math.Pow(2, integrationEvent.Attempts));
        }

        await db.SaveChangesAsync(cancellationToken);
        return integrationEvent;
    }

    public async Task<IntegrationEvent> RetryAsync(int id, CancellationToken cancellationToken)
    {
        var integrationEvent = await db.IntegrationEvents.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException("integration event not found");
        if (integrationEvent.Status != "dead_letter")
        {
            throw new InvalidOperationException("only dead-letter events can be retried");
        }

        integrationEvent.Status = "pending";
        integrationEvent.NextAttemptAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return integrationEvent;
    }

    public Task<List<TenantIntegration>> TenantsAsync(CancellationToken cancellationToken) =>
        db.TenantIntegrations.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);

    public Task<List<IntegrationEvent>> DeadLettersAsync(CancellationToken cancellationToken) =>
        db.IntegrationEvents.AsNoTracking()
            .Where(item => item.Status == "dead_letter")
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

    public static string SerializePayload(object payload) => JsonSerializer.Serialize(payload);
}
