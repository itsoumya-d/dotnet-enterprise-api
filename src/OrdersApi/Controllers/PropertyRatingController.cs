using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/integrations/property-rating")]
public class PropertyRatingController : ControllerBase
{
    private readonly IPropertyRatingMigrationService _migrationService;
    private readonly ILogger<PropertyRatingController> _logger;

    public PropertyRatingController(IPropertyRatingMigrationService migrationService, ILogger<PropertyRatingController> logger)
    {
        _migrationService = migrationService;
        _logger = logger;
    }

    [HttpPost("batch")]
    public async Task<ActionResult<PropertyRatingBatchResult>> IngestBatch([FromBody] PropertyRatingBatchRequest request, CancellationToken cancellationToken)
    {
        if (request.Records == null || request.Records.Count == 0)
        {
            return BadRequest(new { error = "Migration batch must contain at least one property rating record." });
        }

        _logger.LogInformation("Processing property rating migration batch {BatchId} with {Count} records for council {Council}",
            request.BatchId, request.Records.Count, request.CouncilIdentifier);

        var result = await _migrationService.ProcessMigrationBatchAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("batch/{batchId}")]
    public async Task<ActionResult<PropertyRatingBatchResult>> GetBatchStatus(string batchId, CancellationToken cancellationToken)
    {
        var result = await _migrationService.GetBatchStatusAsync(batchId, cancellationToken);
        if (result == null)
        {
            return NotFound(new { error = $"Migration batch '{batchId}' not found." });
        }

        return Ok(result);
    }
}
