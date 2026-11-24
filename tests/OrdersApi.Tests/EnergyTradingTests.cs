using System.Net;
using System.Net.Http.Json;
using OrdersApi.Models;
using OrdersApi.Services;
using Xunit;

namespace OrdersApi.Tests;

public class EnergyTradingTests : IDisposable
{
    private readonly IntegrationApiFactory _factory = new();
    private readonly HttpClient _client;
    private readonly EnergyTradingRiskService _unitService = new();

    public EnergyTradingTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public void Service_EvaluatesAndDispatches_ValidPowerOrder()
    {
        var req = new EnergyTradeOrderRequest(
            OrderId: "pwr-001",
            TenantId: "utility-alpha",
            GridZone: PowerGridZone.EPEX_SPOT_DE,
            ContractType: PowerContractType.DayAhead,
            Side: "Buy",
            VolumeMWh: 50.0m,
            PricePerMWh: 85.5m,
            DeliveryHour: 14
        );

        var result = _unitService.EvaluateAndDispatch(req);

        Assert.Equal("Dispatched", result.Status);
        Assert.True(result.RiskSummary.Approved);
        Assert.Equal(4275.0m, result.RiskSummary.CalculatedNotional);
        Assert.StartsWith("dsp-", result.DispatchId);
    }

    [Fact]
    public void Service_Rejects_NegativePriceWithoutSafeguard()
    {
        var req = new EnergyTradeOrderRequest(
            OrderId: "pwr-002",
            TenantId: "utility-beta",
            GridZone: PowerGridZone.ERCOT,
            ContractType: PowerContractType.Intraday,
            Side: "Sell",
            VolumeMWh: 100.0m,
            PricePerMWh: -12.5m,
            DeliveryHour: 3,
            NegativePriceProtection: false
        );

        var result = _unitService.EvaluateAndDispatch(req);

        Assert.Equal("RejectedByRisk", result.Status);
        Assert.False(result.RiskSummary.Approved);
        Assert.Contains("negative price protection", result.RiskSummary.RejectionReason!);
    }

    [Fact]
    public void Service_Rejects_ExcessiveExposureVolume()
    {
        var req = new EnergyTradeOrderRequest(
            OrderId: "pwr-003",
            TenantId: "utility-gamma",
            GridZone: PowerGridZone.CAISO,
            ContractType: PowerContractType.CapacityMarket,
            Side: "Buy",
            VolumeMWh: 6000.0m, // exceeds 5000 MWh max limit
            PricePerMWh: 50.0m,
            DeliveryHour: 12
        );

        var result = _unitService.EvaluateAndDispatch(req);

        Assert.Equal("RejectedByRisk", result.Status);
        Assert.False(result.RiskSummary.Approved);
        Assert.Contains("exceeds remaining tenant dispatch capacity", result.RiskSummary.RejectionReason!);
    }

    [Fact]
    public async Task Http_SubmitAndRetrieveOrder_Succeeds()
    {
        var req = new EnergyTradeOrderRequest(
            OrderId: "http-pwr-101",
            TenantId: "retailer-delta",
            GridZone: PowerGridZone.N2EX_UK,
            ContractType: PowerContractType.DayAhead,
            Side: "Buy",
            VolumeMWh: 25.0m,
            PricePerMWh: 92.0m,
            DeliveryHour: 18
        );

        var postRes = await _client.PostAsJsonAsync("/api/v1/energy-trading/orders", req);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var dispatch = await postRes.Content.ReadFromJsonAsync<EnergyDispatchResult>();
        Assert.NotNull(dispatch);
        Assert.Equal("Dispatched", dispatch.Status);

        var getRes = await _client.GetAsync($"/api/v1/energy-trading/orders/{req.OrderId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var fetched = await getRes.Content.ReadFromJsonAsync<EnergyDispatchResult>();
        Assert.NotNull(fetched);
        Assert.Equal(req.OrderId, fetched.OrderId);
    }
}
