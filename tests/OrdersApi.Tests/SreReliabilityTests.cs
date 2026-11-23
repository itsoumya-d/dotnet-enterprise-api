using OrdersApi.Controllers;
using OrdersApi.Models;
using OrdersApi.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace OrdersApi.Tests;

public class SreReliabilityTests
{
    [Fact]
    public void EvaluatesSlo_WithZeroEvents_ReturnsHealthyAndFullBudget()
    {
        var service = new SreReliabilityService();
        var slo = new ServiceLevelObjective("OrdersApi", 99.9, 200.0, TimeSpan.FromHours(1));

        var report = service.EvaluateSlo(slo);

        Assert.Equal("OrdersApi", report.ServiceName);
        Assert.Equal(0, report.TotalRequests);
        Assert.Equal(100.0, report.ActualAvailabilityPercentage);
        Assert.Equal(100.0, report.ErrorBudgetRemainingPercentage);
        Assert.Equal(0.0, report.HourlyBurnRate);
        Assert.Equal(SloStatus.Healthy, report.Status);
        Assert.False(report.AutomatedMitigationActive);
    }

    [Fact]
    public void EvaluatesSlo_WithHighFailures_TripsBreachAndTriggersMitigation()
    {
        var service = new SreReliabilityService();
        var slo = new ServiceLevelObjective("OrdersApi", 99.9, 200.0, TimeSpan.FromHours(1));

        // 1000 requests, 15 failures -> 1.5% failure rate (allowed is 0.1%)
        // Burn rate = 1.5 / 0.1 = 15.0x (exceeds critical 14.4x threshold)
        for (int i = 0; i < 985; i++)
        {
            service.RecordEvent("/api/v1/orders", 200, 45.0);
        }
        for (int i = 0; i < 15; i++)
        {
            service.RecordEvent("/api/v1/orders", 500, 350.0);
        }

        var report = service.EvaluateSlo(slo);

        Assert.Equal(1000, report.TotalRequests);
        Assert.Equal(985, report.SuccessfulRequests);
        Assert.Equal(15, report.FailedRequests);
        Assert.True(report.HourlyBurnRate >= 14.4);
        Assert.Equal(0.0, report.ErrorBudgetRemainingPercentage);
        Assert.Equal(SloStatus.Breached, report.Status);
        Assert.True(report.AutomatedMitigationActive);
    }

    [Fact]
    public void EvaluatesSlo_WithHighLatency_TransitionsToDegraded()
    {
        var service = new SreReliabilityService();
        var slo = new ServiceLevelObjective("OrdersApi", 99.9, 150.0, TimeSpan.FromHours(1));

        // 100 successful requests, but P95 latency is 250ms (exceeds target 150ms)
        for (int i = 0; i < 90; i++)
        {
            service.RecordEvent("/api/v1/orders", 200, 80.0);
        }
        for (int i = 0; i < 10; i++)
        {
            service.RecordEvent("/api/v1/orders", 200, 260.0);
        }

        var report = service.EvaluateSlo(slo);

        Assert.Equal(100, report.TotalRequests);
        Assert.Equal(100.0, report.ActualAvailabilityPercentage);
        Assert.True(report.P95LatencyMs > 150.0);
        Assert.Equal(SloStatus.Degraded, report.Status);
    }

    [Fact]
    public void Controller_GetSloReport_ReturnsValidActionResult()
    {
        var service = new SreReliabilityService();
        service.RecordEvent("/api/v1/orders", 200, 35.0);

        var controller = new SreObservabilityController(service);
        var result = controller.GetSloReport("OrdersApi", 99.9, 200.0, 60);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<SloHealthReport>(okResult.Value);

        Assert.Equal("OrdersApi", report.ServiceName);
        Assert.Equal(1, report.TotalRequests);
    }
}
