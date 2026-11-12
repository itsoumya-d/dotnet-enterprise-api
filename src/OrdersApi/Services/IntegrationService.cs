using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;
using OrdersApi.Models;

namespace OrdersApi.Services;

public class IntegrationService(
    OrdersDbContext db,
    IIntegrationDispatcher dispatcher,
    TimeProvider? timeProvider = null,
    IAuditStore? auditStore = null,
    IEventPublisher? eventPublisher = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly IAuditStore _audit = auditStore ?? new InMemoryAuditStore();
    private readonly IEventPublisher _publisher = eventPublisher ?? new NoopEventPublisher();

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
            var now = _time.GetUtcNow();
            integrationEvent.NextAttemptAt = now;
            var tenant = await db.TenantIntegrations.FindAsync([integrationEvent.TenantIntegrationId], cancellationToken);
            if (tenant is not null)
            {
                tenant.LastSyncedAt = now;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            integrationEvent.LastError = ex.Message;
            integrationEvent.Status = integrationEvent.Attempts >= 3 ? "dead_letter" : "pending";
            integrationEvent.NextAttemptAt = _time.GetUtcNow().AddSeconds(Math.Pow(2, integrationEvent.Attempts));
        }

        await db.SaveChangesAsync(cancellationToken);
        await EmitAuditAsync(
            integrationEvent.Status switch
            {
                "processed" => "integration.processed",
                "dead_letter" => "integration.dead_letter",
                _ => "integration.retry_scheduled"
            },
            new Dictionary<string, object?>
            {
                ["eventId"] = integrationEvent.Id,
                ["tenantIntegrationId"] = integrationEvent.TenantIntegrationId,
                ["eventType"] = integrationEvent.EventType,
                ["attempts"] = integrationEvent.Attempts,
                ["status"] = integrationEvent.Status,
                ["lastError"] = integrationEvent.LastError
            },
            cancellationToken);
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
        await EmitAuditAsync(
            "integration.dead_letter_requeued",
            new Dictionary<string, object?>
            {
                ["eventId"] = integrationEvent.Id,
                ["tenantIntegrationId"] = integrationEvent.TenantIntegrationId,
                ["eventType"] = integrationEvent.EventType
            },
            cancellationToken);
        return integrationEvent;
    }

    public async Task<DeadLetterReplayResult> ReplayDeadLettersAsync(ReplayDeadLettersRequest request, CancellationToken cancellationToken)
    {
        if (request.MaxCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaxCount), "maxCount must be between 1 and 100");
        }

        if (request.TenantIntegrationId.HasValue)
        {
            var exists = await db.TenantIntegrations.AnyAsync(t => t.Id == request.TenantIntegrationId.Value, cancellationToken);
            if (!exists)
            {
                throw new KeyNotFoundException("tenant integration not found");
            }
        }

        var query = db.IntegrationEvents.Where(item => item.Status == "dead_letter");
        if (request.TenantIntegrationId.HasValue)
        {
            query = query.Where(item => item.TenantIntegrationId == request.TenantIntegrationId.Value);
        }

        var events = await query.OrderBy(item => item.Id).Take(request.MaxCount).ToListAsync(cancellationToken);
        var now = _time.GetUtcNow();
        foreach (var item in events)
        {
            item.Status = "pending";
            item.NextAttemptAt = now;
        }

        if (events.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var ids = events.Select(e => e.Id).ToList();
        await EmitAuditAsync(
            "integration.dead_letters_replayed",
            new Dictionary<string, object?>
            {
                ["tenantIntegrationId"] = request.TenantIntegrationId,
                ["requeuedCount"] = ids.Count,
                ["eventIds"] = ids
            },
            cancellationToken);
        return new DeadLetterReplayResult(ids.Count, ids);
    }

    public async Task<TenantIntegrationSummary> TenantSummaryAsync(int tenantIntegrationId, CancellationToken cancellationToken)
    {
        var tenant = await db.TenantIntegrations.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tenantIntegrationId, cancellationToken)
            ?? throw new KeyNotFoundException("tenant integration not found");

        var counts = await db.IntegrationEvents.AsNoTracking()
            .Where(e => e.TenantIntegrationId == tenantIntegrationId)
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountFor(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        var processed = CountFor("processed");
        var pending = CountFor("pending");
        var deadLetter = CountFor("dead_letter");
        return new TenantIntegrationSummary(
            tenant.Id,
            tenant.TenantName,
            tenant.SourceSystem,
            tenant.TargetSystem,
            processed + pending + deadLetter,
            processed,
            pending,
            deadLetter,
            tenant.LastSyncedAt,
            deadLetter > 0 ? "degraded" : "healthy");
    }

    public Task<IReadOnlyList<AuditEvent>> AuditTrailAsync(int limit, CancellationToken cancellationToken) =>
        _audit.ListAsync(Math.Clamp(limit, 1, 200), cancellationToken);

    private async Task EmitAuditAsync(string type, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var auditEvent = new AuditEvent
        {
            Type = type,
            Payload = payload,
            OccurredAt = _time.GetUtcNow()
        };
        await _audit.AppendAsync(auditEvent, cancellationToken);
        await _publisher.PublishAsync(auditEvent, cancellationToken);
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
