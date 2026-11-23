using System.Collections.Concurrent;
using OrdersApi.Models;

namespace OrdersApi.Services;

public class SreReliabilityService
{
    private readonly ConcurrentBag<TelemetryEvent> _events = new();

    public void RecordEvent(string endpoint, int statusCode, double durationMs)
    {
        _events.Add(new TelemetryEvent(endpoint, statusCode, durationMs, DateTime.UtcNow));
    }

    public void Reset()
    {
        _events.Clear();
    }

    public SloHealthReport EvaluateSlo(ServiceLevelObjective slo)
    {
        var cutoff = DateTime.UtcNow - slo.WindowDuration;
        var windowEvents = _events.Where(e => e.TimestampUtc >= cutoff).ToList();

        if (windowEvents.Count == 0)
        {
            return new SloHealthReport(
                ServiceName: slo.ServiceName,
                TotalRequests: 0,
                SuccessfulRequests: 0,
                FailedRequests: 0,
                ActualAvailabilityPercentage: 100.0,
                TargetAvailabilityPercentage: slo.TargetAvailabilityPercentage,
                P95LatencyMs: 0.0,
                P99LatencyMs: 0.0,
                ErrorBudgetRemainingPercentage: 100.0,
                HourlyBurnRate: 0.0,
                Status: SloStatus.Healthy,
                AutomatedMitigationActive: false,
                EvaluatedAtUtc: DateTime.UtcNow
            );
        }

        long total = windowEvents.Count;
        long failures = windowEvents.Count(e => e.StatusCode >= 500);
        long successes = total - failures;

        double actualAvailability = (double)successes / total * 100.0;
        double allowedFailureRate = (100.0 - slo.TargetAvailabilityPercentage) / 100.0;
        double actualFailureRate = (double)failures / total;

        // Error budget consumed vs remaining
        double budgetRemaining;
        if (allowedFailureRate <= 0)
        {
            budgetRemaining = failures == 0 ? 100.0 : 0.0;
        }
        else
        {
            double consumed = (actualFailureRate / allowedFailureRate) * 100.0;
            budgetRemaining = Math.Max(0.0, 100.0 - consumed);
        }

        // Multi-window burn rate calculation (actual error rate / allowed error rate)
        double burnRate = allowedFailureRate > 0 ? actualFailureRate / allowedFailureRate : 0.0;

        // Latency percentiles
        var latencies = windowEvents.Select(e => e.DurationMs).OrderBy(x => x).ToList();
        double p95 = GetPercentile(latencies, 0.95);
        double p99 = GetPercentile(latencies, 0.99);

        // Status evaluation: Breached if budget depleted or burn rate critical (> 14.4x)
        SloStatus status = SloStatus.Healthy;
        bool mitigationActive = false;

        if (budgetRemaining <= 0 || burnRate >= 14.4)
        {
            status = SloStatus.Breached;
            mitigationActive = true;
        }
        else if (budgetRemaining < 50.0 || burnRate >= 2.0 || p95 > slo.TargetP95LatencyMs)
        {
            status = SloStatus.Degraded;
            mitigationActive = burnRate >= 6.0;
        }

        return new SloHealthReport(
            ServiceName: slo.ServiceName,
            TotalRequests: total,
            SuccessfulRequests: successes,
            FailedRequests: failures,
            ActualAvailabilityPercentage: Math.Round(actualAvailability, 3),
            TargetAvailabilityPercentage: slo.TargetAvailabilityPercentage,
            P95LatencyMs: Math.Round(p95, 2),
            P99LatencyMs: Math.Round(p99, 2),
            ErrorBudgetRemainingPercentage: Math.Round(budgetRemaining, 2),
            HourlyBurnRate: Math.Round(burnRate, 2),
            Status: status,
            AutomatedMitigationActive: mitigationActive,
            EvaluatedAtUtc: DateTime.UtcNow
        );
    }

    private static double GetPercentile(List<double> sortedSequence, double percentile)
    {
        if (sortedSequence.Count == 0) return 0.0;
        int idx = (int)Math.Ceiling(percentile * sortedSequence.Count) - 1;
        idx = Math.Clamp(idx, 0, sortedSequence.Count - 1);
        return sortedSequence[idx];
    }
}
