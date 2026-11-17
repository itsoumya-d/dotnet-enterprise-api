using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/erp/business-central")]
public sealed class BusinessCentralController : ControllerBase
{
    private readonly BusinessCentralIntegrationService _service;

    public BusinessCentralController(BusinessCentralIntegrationService service)
    {
        _service = service;
    }

    [HttpPost("orders/sync")]
    public ActionResult<BusinessCentralSyncResult> SyncOrder([FromBody] BusinessCentralOrderSyncRequest request)
    {
        try
        {
            var result = _service.SyncOrder(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("orders/by-ref/{externalReference}")]
    public ActionResult<BusinessCentralSyncResult> GetByExternalRef(string externalReference)
    {
        var result = _service.GetOrderByExternalRef(externalReference);
        if (result == null)
            return NotFound(new { error = $"Order with external reference {externalReference} not found." });

        return Ok(result);
    }

    [HttpPost("inventory/reconcile")]
    public ActionResult<ReconciliationReport> ReconcileInventory([FromBody] InventoryReconciliationBatch batch)
    {
        try
        {
            var report = _service.ReconcileInventory(batch);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("reconciliations/{batchId}")]
    public ActionResult<ReconciliationReport> GetReconciliation(string batchId)
    {
        var report = _service.GetReconciliation(batchId);
        if (report == null)
            return NotFound(new { error = $"Reconciliation batch {batchId} not found." });

        return Ok(report);
    }

    [HttpPost("dead-letter/recover")]
    public ActionResult<DeadLetterRecoveryResult> RecoverDeadLetters([FromBody] DeadLetterRecoveryRequest request)
    {
        try
        {
            var result = _service.RecoverDeadLetters(request);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
