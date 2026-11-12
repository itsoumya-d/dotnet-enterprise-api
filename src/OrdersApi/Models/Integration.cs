using System.Text.Json.Serialization;

namespace OrdersApi.Models;

public class TenantIntegration
{
    public int Id { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string TargetSystem { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; set; }
    [JsonIgnore]
    public List<IntegrationEvent> Events { get; set; } = new();
}

public class IntegrationEvent
{
    public int Id { get; set; }
    public int TenantIntegrationId { get; set; }
    [JsonIgnore]
    public TenantIntegration? TenantIntegration { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public record CreateTenantRequest(string TenantName, string SourceSystem, string TargetSystem);
public record EnqueueIntegrationEventRequest(int TenantIntegrationId, string EventType, string IdempotencyKey, string Payload);
public record ReplayDeadLettersRequest(int? TenantIntegrationId = null, int MaxCount = 50);
public record DeadLetterReplayResult(int RequeuedCount, List<int> EventIds);
public record TenantIntegrationSummary(
    int TenantIntegrationId,
    string TenantName,
    string SourceSystem,
    string TargetSystem,
    int TotalEvents,
    int ProcessedCount,
    int PendingCount,
    int DeadLetterCount,
    DateTimeOffset? LastSyncedAt,
    string HealthStatus);
