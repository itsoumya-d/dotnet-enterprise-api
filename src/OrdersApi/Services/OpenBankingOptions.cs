namespace OrdersApi.Services;

public class OpenBankingOptions
{
    public string BearerToken { get; set; } = "dev-token";
    public string? AzureServiceBusConnectionString { get; set; }
    public string AzureQueue { get; set; } = "payments";
    public string? CosmosConnectionString { get; set; }
    public string CosmosDatabase { get; set; } = "ledger";
    public string CosmosContainer { get; set; } = "audit";
}
