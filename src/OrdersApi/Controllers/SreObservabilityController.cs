using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/v1/sre")]
public class SreObservabilityController : ControllerBase
{
    private readonly SreReliabilityService _reliabilityService;

    public SreObservabilityController(SreReliabilityService reliabilityService)
    {
        _reliabilityService = reliabilityService;
    }

    [HttpGet("slo-report")]
    public ActionResult<SloHealthReport> GetSloReport(
        [FromQuery] string serviceName = "OrdersApi",
        [FromQuery] double targetAvailability = 99.9,
        [FromQuery] double targetP95Ms = 200.0,
        [FromQuery] int windowMinutes = 60)
    {
        var slo = new ServiceLevelObjective(
            ServiceName: serviceName,
            TargetAvailabilityPercentage: targetAvailability,
            TargetP95LatencyMs: targetP95Ms,
            WindowDuration: TimeSpan.FromMinutes(windowMinutes)
        );

        var report = _reliabilityService.EvaluateSlo(slo);
        return Ok(report);
    }

    [HttpPost("record-metric")]
    public IActionResult RecordMetric([FromBody] TelemetryEvent telemetry)
    {
        _reliabilityService.RecordEvent(telemetry.Endpoint, telemetry.StatusCode, telemetry.DurationMs);
        return Accepted();
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        _reliabilityService.Reset();
        return NoContent();
    }
}
