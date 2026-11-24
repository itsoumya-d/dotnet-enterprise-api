using OrdersApi.Models;
using OrdersApi.Services;
using Xunit;

namespace OrdersApi.Tests;

public sealed class CareTelemetryTests
{
    private sealed class TrackingEventPublisher : IEventPublisher
    {
        public List<AuditEvent> PublishedEvents { get; } = new();

        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task IngestBatch_ValidObservations_IngestsAllAndReturnsSummary()
    {
        var publisher = new TrackingEventPublisher();
        var service = new CareTelemetryService(publisher);

        var request = new CareTelemetryBatchRequest
        {
            DeviceOrGatewayId = "gw-care-wing-north-01",
            Observations = new()
            {
                new CareObservation
                {
                    ResidentId = "res-101",
                    ObservationType = "HeartRate",
                    Value = 72,
                    Unit = "bpm"
                },
                new CareObservation
                {
                    ResidentId = "res-101",
                    ObservationType = "BloodOxygen",
                    Value = 98,
                    Unit = "%"
                }
            }
        };

        var result = await service.IngestTelemetryBatchAsync(request);

        Assert.Equal(2, result.TotalReceived);
        Assert.Equal(2, result.Ingested);
        Assert.Equal(0, result.DuplicatesSkipped);
        Assert.Equal(0, result.CriticalAlertsTriggered);
        Assert.Empty(publisher.PublishedEvents);

        var history = await service.GetResidentObservationsAsync("res-101");
        Assert.Equal(2, history.Count);
    }

    [Theory]
    [InlineData("HeartRate", 135, "Critical")]
    [InlineData("HeartRate", 45, "Critical")]
    [InlineData("HeartRate", 55, "Advisory")]
    [InlineData("HeartRate", 75, "Normal")]
    [InlineData("BloodOxygen", 88, "Critical")]
    [InlineData("BloodOxygen", 92, "Advisory")]
    [InlineData("BloodOxygen", 97, "Normal")]
    [InlineData("MobilityScore", 18, "Advisory")]
    [InlineData("MobilityScore", 85, "Normal")]
    public void EvaluateObservation_PhysiologicalMetrics_AssignsExpectedStatus(string metric, double value, string expectedStatus)
    {
        var service = new CareTelemetryService(new NoopEventPublisher());
        var obs = new CareObservation
        {
            ResidentId = "res-202",
            ObservationType = metric,
            Value = value,
            Unit = "unit"
        };

        var evaluated = service.EvaluateObservation(obs);

        Assert.Equal(expectedStatus, evaluated.Status);
    }

    [Fact]
    public async Task IngestBatch_CriticalObservations_PublishesAlertsToEventPublisher()
    {
        var publisher = new TrackingEventPublisher();
        var service = new CareTelemetryService(publisher);

        var request = new CareTelemetryBatchRequest
        {
            DeviceOrGatewayId = "gw-icu-east-02",
            Observations = new()
            {
                new CareObservation
                {
                    ResidentId = "res-303",
                    ObservationType = "BloodOxygen",
                    Value = 86,
                    Unit = "%"
                },
                new CareObservation
                {
                    ResidentId = "res-303",
                    ObservationType = "HeartRate",
                    Value = 138,
                    Unit = "bpm"
                }
            }
        };

        var result = await service.IngestTelemetryBatchAsync(request);

        Assert.Equal(2, result.CriticalAlertsTriggered);
        Assert.Equal(2, publisher.PublishedEvents.Count);
        Assert.All(publisher.PublishedEvents, e => Assert.Equal("CareTelemetry.CriticalAlert", e.Type));
    }

    [Fact]
    public async Task IngestBatch_DuplicateIdempotencyKeys_SkipsDuplicates()
    {
        var publisher = new TrackingEventPublisher();
        var service = new CareTelemetryService(publisher);

        var obs = new CareObservation
        {
            ResidentId = "res-404",
            ObservationType = "HeartRate",
            Value = 74,
            Unit = "bpm",
            IdempotencyKey = "tx-batch-dedupe-key-99"
        };

        var request = new CareTelemetryBatchRequest
        {
            DeviceOrGatewayId = "gw-edge-03",
            Observations = new() { obs }
        };

        var firstResult = await service.IngestTelemetryBatchAsync(request);
        Assert.Equal(1, firstResult.Ingested);
        Assert.Equal(0, firstResult.DuplicatesSkipped);

        var secondResult = await service.IngestTelemetryBatchAsync(request);
        Assert.Equal(0, secondResult.Ingested);
        Assert.Equal(1, secondResult.DuplicatesSkipped);
    }

    [Fact]
    public async Task GetResidentObservations_FilteredByType_ReturnsMatchingMetrics()
    {
        var service = new CareTelemetryService(new NoopEventPublisher());
        var request = new CareTelemetryBatchRequest
        {
            DeviceOrGatewayId = "gw-wing-05",
            Observations = new()
            {
                new CareObservation { ResidentId = "res-505", ObservationType = "HeartRate", Value = 70, Unit = "bpm" },
                new CareObservation { ResidentId = "res-505", ObservationType = "BloodOxygen", Value = 99, Unit = "%" },
                new CareObservation { ResidentId = "res-505", ObservationType = "HeartRate", Value = 76, Unit = "bpm" },
                new CareObservation { ResidentId = "res-606", ObservationType = "HeartRate", Value = 80, Unit = "bpm" }
            }
        };

        await service.IngestTelemetryBatchAsync(request);

        var hrObservations = await service.GetResidentObservationsAsync("res-505", observationType: "HeartRate");
        Assert.Equal(2, hrObservations.Count);
        Assert.All(hrObservations, o => Assert.Equal("HeartRate", o.ObservationType));
    }

    [Fact]
    public void EvaluateObservation_NegativeValue_ThrowsArgumentOutOfRangeException()
    {
        var service = new CareTelemetryService(new NoopEventPublisher());
        var invalidObs = new CareObservation
        {
            ResidentId = "res-707",
            ObservationType = "HeartRate",
            Value = -10,
            Unit = "bpm"
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => service.EvaluateObservation(invalidObs));
    }
}
