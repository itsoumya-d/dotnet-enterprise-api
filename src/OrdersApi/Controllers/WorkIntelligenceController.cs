using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/work-intelligence")]
public class WorkIntelligenceController : ControllerBase
{
    private readonly IWorkIntelligenceService _service;
    private readonly ILogger<WorkIntelligenceController> _logger;

    public WorkIntelligenceController(IWorkIntelligenceService service, ILogger<WorkIntelligenceController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(WorkIntelligenceAnalysisResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze([FromBody] TenantCommunicationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MessageBody) || string.IsNullOrWhiteSpace(request.PropertyId))
        {
            return BadRequest(new { error = "PropertyId and MessageBody are required fields." });
        }

        try
        {
            var result = await _service.AnalyzeCommunicationAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("clauses/{propertyId}")]
    [ProducesResponseType(typeof(IReadOnlyList<LeaseClause>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClauses([FromRoute] string propertyId, CancellationToken cancellationToken)
    {
        var clauses = await _service.GetLeaseClausesAsync(propertyId, cancellationToken);
        return Ok(clauses);
    }
}
