namespace OrdersApi.Models;

public enum PowerContractType { DayAhead, Intraday, FrequencyResponse, CapacityMarket }
public enum PowerGridZone { PJM, ERCOT, CAISO, EPEX_SPOT_DE, N2EX_UK }

public record EnergyTradeOrderRequest(
    string OrderId, string TenantId, PowerGridZone GridZone, PowerContractType ContractType,
    string Side, decimal VolumeMWh, decimal PricePerMWh, int DeliveryHour, bool NegativePriceProtection = true
);

public record EnergyRiskAssessment(bool Approved, decimal CalculatedNotional, decimal CumulativeTenantExposureMWh, string? RejectionReason = null);
public record EnergyDispatchResult(string DispatchId, string OrderId, string TenantId, string Status, EnergyRiskAssessment RiskSummary, DateTimeOffset Timestamp);
public record TenantEnergyExposure(string TenantId, decimal TotalDispatchedMWh, decimal TotalNotionalValue, int TotalTradesCount);
