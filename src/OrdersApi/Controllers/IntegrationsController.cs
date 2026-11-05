using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/integrations")]
public class IntegrationsController(IntegrationService service) : ControllerBase
{
    [HttpPost("tenants")]
    public async Task<ActionResult<TenantIntegration>> CreateTenant(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return CreatedAtAction(nameof(Tenants), await service.CreateTenantAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("tenants")]
    public async Task<ActionResult<List<TenantIntegration>>> Tenants(CancellationToken cancellationToken) =>
        await service.TenantsAsync(cancellationToken);

    [HttpPost("events")]
    public async Task<ActionResult<IntegrationEvent>> Enqueue(EnqueueIntegrationEventRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await service.EnqueueAsync(request, cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("events/{id:int}/process")]
    public async Task<ActionResult<IntegrationEvent>> Process(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await service.ProcessAsync(id, cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("events/{id:int}/retry")]
    public async Task<ActionResult<IntegrationEvent>> Retry(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await service.RetryAsync(id, cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet("events/dead-letters")]
    public async Task<ActionResult<List<IntegrationEvent>>> DeadLetters(CancellationToken cancellationToken) =>
        await service.DeadLettersAsync(cancellationToken);
}
