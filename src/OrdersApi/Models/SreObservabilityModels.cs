namespace OrdersApi.Models;

public record ServiceLevelObjective(
    string ServiceName,
    double TargetAvailabilityPercentage,
    double TargetP95LatencyMs,
    TimeSpan WindowDuration
);

public enum SloStatus
{
    Healthy,
    Degraded,
    Breached
}

public record SloHealthReport(
    string ServiceName,
    long TotalRequests,
    long SuccessfulRequests,
    long FailedRequests,
    double ActualAvailabilityPercentage,
    double TargetAvailabilityPercentage,
    double P95LatencyMs,
    double P99LatencyMs,
    double ErrorBudgetRemainingPercentage,
    double HourlyBurnRate,
    SloStatus Status,
    bool AutomatedMitigationActive,
    DateTime EvaluatedAtUtc
);

public record TelemetryEvent(
    string Endpoint,
    int StatusCode,
    double DurationMs,
    DateTime TimestampUtc
);
