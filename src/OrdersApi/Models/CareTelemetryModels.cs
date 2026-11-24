namespace OrdersApi.Models;

public sealed record CareObservation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ResidentId { get; init; }
    public required string ObservationType { get; init; }
    public required double Value { get; init; }
    public required string Unit { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset IngestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Status { get; init; } = "Normal";
    public string? IdempotencyKey { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public sealed record CareTelemetryBatchRequest
{
    public required string DeviceOrGatewayId { get; init; }
    public required List<CareObservation> Observations { get; init; } = new();
}

public sealed record CareTelemetryIngestionResult
{
    public required int TotalReceived { get; init; }
    public required int Ingested { get; init; }
    public required int DuplicatesSkipped { get; init; }
    public required int CriticalAlertsTriggered { get; init; }
    public required List<string> DispatchedEventIds { get; init; } = new();
}
