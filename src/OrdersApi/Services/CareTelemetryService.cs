using System.Collections.Concurrent;
using OrdersApi.Models;

namespace OrdersApi.Services;

public interface ICareTelemetryService
{
    Task<CareTelemetryIngestionResult> IngestTelemetryBatchAsync(CareTelemetryBatchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CareObservation>> GetResidentObservationsAsync(string residentId, string? observationType = null, int limit = 50);
    CareObservation EvaluateObservation(CareObservation observation);
}

public sealed class CareTelemetryService : ICareTelemetryService
{
    private readonly IEventPublisher _publisher;
    private readonly ConcurrentDictionary<string, CareObservation> _observations = new();
    private readonly ConcurrentDictionary<string, bool> _processedIdempotencyKeys = new();

    public CareTelemetryService(IEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public CareObservation EvaluateObservation(CareObservation observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ResidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ObservationType);

        if (observation.Value < 0 && observation.ObservationType != "TemperatureOffset")
        {
            throw new ArgumentOutOfRangeException(nameof(observation), "Observation value cannot be negative for physiological metrics.");
        }

        var status = observation.ObservationType switch
        {
            "HeartRate" => observation.Value switch
            {
                < 50 or > 120 => "Critical",
                < 60 or > 100 => "Advisory",
                _ => "Normal"
            },
            "BloodOxygen" => observation.Value switch
            {
                < 90 => "Critical",
                < 95 => "Advisory",
                _ => "Normal"
            },
            "MobilityScore" => observation.Value < 25 ? "Advisory" : "Normal",
            _ => "Normal"
        };

        return observation with { Status = status, IngestedAt = DateTimeOffset.UtcNow };
    }

    public async Task<CareTelemetryIngestionResult> IngestTelemetryBatchAsync(CareTelemetryBatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceOrGatewayId);

        var total = request.Observations.Count;
        var ingested = 0;
        var skipped = 0;
        var criticalAlerts = 0;
        var dispatchedEventIds = new List<string>();

        foreach (var obs in request.Observations)
        {
            if (!string.IsNullOrWhiteSpace(obs.IdempotencyKey) && !_processedIdempotencyKeys.TryAdd(obs.IdempotencyKey, true))
            {
                skipped++;
                continue;
            }

            var evaluated = EvaluateObservation(obs);
            _observations[evaluated.Id] = evaluated;
            ingested++;

            if (evaluated.Status is "Critical" or "Advisory")
            {
                if (evaluated.Status == "Critical")
                {
                    criticalAlerts++;
                }

                var auditEvent = new AuditEvent
                {
                    Type = evaluated.Status == "Critical" ? "CareTelemetry.CriticalAlert" : "CareTelemetry.AdvisoryAlert",
                    Payload = new Dictionary<string, object?>
                    {
                        ["observationId"] = evaluated.Id,
                        ["residentId"] = evaluated.ResidentId,
                        ["type"] = evaluated.ObservationType,
                        ["value"] = evaluated.Value,
                        ["unit"] = evaluated.Unit,
                        ["gatewayId"] = request.DeviceOrGatewayId,
                        ["severity"] = evaluated.Status
                    }
                };

                await _publisher.PublishAsync(auditEvent, cancellationToken);
                dispatchedEventIds.Add(auditEvent.Id);
            }
        }

        return new CareTelemetryIngestionResult
        {
            TotalReceived = total,
            Ingested = ingested,
            DuplicatesSkipped = skipped,
            CriticalAlertsTriggered = criticalAlerts,
            DispatchedEventIds = dispatchedEventIds
        };
    }

    public Task<IReadOnlyList<CareObservation>> GetResidentObservationsAsync(string residentId, string? observationType = null, int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(residentId);

        var query = _observations.Values
            .Where(o => string.Equals(o.ResidentId, residentId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(observationType))
        {
            query = query.Where(o => string.Equals(o.ObservationType, observationType, StringComparison.OrdinalIgnoreCase));
        }

        var results = query
            .OrderByDescending(o => o.RecordedAt)
            .Take(Math.Max(1, Math.Min(limit, 500)))
            .ToList();

        return Task.FromResult<IReadOnlyList<CareObservation>>(results);
    }
}
