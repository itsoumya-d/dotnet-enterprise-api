using System.Collections.Concurrent;
using OrdersApi.Models;

namespace OrdersApi.Services;

public class EnergyTradingRiskService
{
    private readonly ConcurrentDictionary<string, EnergyDispatchResult> _orders = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<EnergyDispatchResult>> _tenantDispatches = new();
    private const decimal MaxTenantCycleExposureMWh = 5000m;
    private const decimal MaxAllowablePricePerMWh = 3000m;

    public EnergyDispatchResult EvaluateAndDispatch(EnergyTradeOrderRequest req)
    {
        if (req.VolumeMWh <= 0)
        {
            return BuildResult(req, false, 0, "Trade volume must be strictly positive (MWh > 0).");
        }

        if (req.PricePerMWh < 0 && !req.NegativePriceProtection)
        {
            return BuildResult(req, false, 0, "Negative price submitted without mandatory negative price protection safeguard.");
        }

        if (req.PricePerMWh > MaxAllowablePricePerMWh)
        {
            return BuildResult(req, false, 0, $"Price {req.PricePerMWh} exceeds market clearing safety cap ({MaxAllowablePricePerMWh}).");
        }

        var tenantBag = _tenantDispatches.GetOrAdd(req.TenantId, _ => new ConcurrentBag<EnergyDispatchResult>());
        decimal currentTenantMWh = tenantBag.Sum(d => d.RiskSummary.CalculatedNotional > 0 ? req.VolumeMWh : 0);

        if (currentTenantMWh + req.VolumeMWh > MaxTenantCycleExposureMWh)
        {
            return BuildResult(req, false, currentTenantMWh, $"Order volume ({req.VolumeMWh} MWh) exceeds remaining tenant dispatch capacity (Max: {MaxTenantCycleExposureMWh} MWh).");
        }

        decimal notional = Math.Abs(req.VolumeMWh * req.PricePerMWh);
        var result = new EnergyDispatchResult(
            DispatchId: $"dsp-{Guid.NewGuid():N}",
            OrderId: req.OrderId,
            TenantId: req.TenantId,
            Status: "Dispatched",
            RiskSummary: new EnergyRiskAssessment(true, notional, currentTenantMWh + req.VolumeMWh),
            Timestamp: DateTimeOffset.UtcNow
        );

        _orders[req.OrderId] = result;
        tenantBag.Add(result);
        return result;
    }

    public EnergyDispatchResult? GetOrder(string orderId) =>
        _orders.TryGetValue(orderId, out var result) ? result : null;

    public TenantEnergyExposure GetTenantExposure(string tenantId)
    {
        if (!_tenantDispatches.TryGetValue(tenantId, out var bag))
        {
            return new TenantEnergyExposure(tenantId, 0, 0, 0);
        }

        var items = bag.ToList();
        return new TenantEnergyExposure(
            TenantId: tenantId,
            TotalDispatchedMWh: items.Sum(i => i.RiskSummary.CalculatedNotional > 0 ? i.RiskSummary.CumulativeTenantExposureMWh : 0),
            TotalNotionalValue: items.Sum(i => i.RiskSummary.CalculatedNotional),
            TotalTradesCount: items.Count
        );
    }

    public void Reset()
    {
        _orders.Clear();
        _tenantDispatches.Clear();
    }

    private static EnergyDispatchResult BuildResult(EnergyTradeOrderRequest req, bool approved, decimal exposure, string reason) =>
        new(
            DispatchId: string.Empty,
            OrderId: req.OrderId,
            TenantId: req.TenantId,
            Status: "RejectedByRisk",
            RiskSummary: new EnergyRiskAssessment(approved, 0, exposure, reason),
            Timestamp: DateTimeOffset.UtcNow
        );
}
