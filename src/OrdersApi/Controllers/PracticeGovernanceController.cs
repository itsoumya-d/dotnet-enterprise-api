using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/practice/governance")]
public class PracticeGovernanceController : ControllerBase
{
    private readonly IEngineeringPracticeService _practiceService;

    public PracticeGovernanceController(IEngineeringPracticeService practiceService)
    {
        _practiceService = practiceService;
    }

    [HttpPost("review-change")]
    public ActionResult<PracticeReviewSummary> ReviewChange([FromBody] CodeChangeReviewRequest request)
    {
        if (request == null || request.Files == null)
        {
            return BadRequest(new { error = "Review request must include pull request metadata and files." });
        }

        var summary = _practiceService.EvaluateChange(request);
        return Ok(summary);
    }

    [HttpPost("record-deployment")]
    public IActionResult RecordDeployment([FromBody] DoraDeploymentEvent deployment)
    {
        if (deployment == null || string.IsNullOrWhiteSpace(deployment.Squad))
        {
            return BadRequest(new { error = "Deployment event must specify squad and deployment details." });
        }

        _practiceService.RecordDeployment(deployment);
        return Accepted(new { status = "recorded", deploymentId = deployment.DeploymentId });
    }

    [HttpGet("dora-metrics/{squad}")]
    public ActionResult<DoraMetricsReport> GetDoraMetrics(string squad, [FromQuery] int windowDays = 30)
    {
        if (string.IsNullOrWhiteSpace(squad))
        {
            return BadRequest(new { error = "Squad name is required." });
        }

        var report = _practiceService.GetDoraMetrics(squad, windowDays);
        return Ok(report);
    }

    [HttpGet("standards-handbook")]
    public ActionResult<IReadOnlyList<EngineeringStandard>> GetHandbook()
    {
        return Ok(_practiceService.GetStandardsHandbook());
    }
}
