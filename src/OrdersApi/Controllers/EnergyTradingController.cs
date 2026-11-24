using Microsoft.AspNetCore.Mvc;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/v1/energy-trading")]
public class EnergyTradingController : ControllerBase
{
    private readonly EnergyTradingRiskService _riskService;

    public EnergyTradingController(EnergyTradingRiskService riskService)
    {
        _riskService = riskService;
    }

    [HttpPost("orders")]
    public ActionResult<EnergyDispatchResult> SubmitOrder([FromBody] EnergyTradeOrderRequest request)
    {
        var result = _riskService.EvaluateAndDispatch(request);
        if (!result.RiskSummary.Approved)
        {
            return UnprocessableEntity(result);
        }

        return CreatedAtAction(nameof(GetOrder), new { orderId = result.OrderId }, result);
    }

    [HttpGet("orders/{orderId}")]
    public ActionResult<EnergyDispatchResult> GetOrder(string orderId)
    {
        var order = _riskService.GetOrder(orderId);
        if (order is null)
        {
            return NotFound(new { error = $"Order {orderId} not found." });
        }

        return Ok(order);
    }

    [HttpGet("tenants/{tenantId}/exposure")]
    public ActionResult<TenantEnergyExposure> GetTenantExposure(string tenantId)
    {
        return Ok(_riskService.GetTenantExposure(tenantId));
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        _riskService.Reset();
        return NoContent();
    }
}
