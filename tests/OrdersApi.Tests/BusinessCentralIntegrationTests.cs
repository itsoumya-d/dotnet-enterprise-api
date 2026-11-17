using System.Net;
using System.Net.Http.Json;
using OrdersApi.Models;
using OrdersApi.Services;
using Xunit;

namespace OrdersApi.Tests;

public class BusinessCentralIntegrationTests : IDisposable
{
    private readonly IntegrationApiFactory _factory = new();
    private readonly HttpClient _client;
    private readonly BusinessCentralIntegrationService _unitService = new();

    public BusinessCentralIntegrationTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task SyncOrder_CreatesNewBusinessCentralDocument_WhenValid()
    {
        var request = new BusinessCentralOrderSyncRequest(
            ExternalReference: "SHOPIFY-NZ-10091",
            SalesChannel: "ShopifyPlus-NZ",
            CustomerEmail: "customer@mitoq.com",
            DestinationCountry: "NZ",
            TotalAmount: 179.95m,
            Currency: "NZD",
            LineItems: new List<BusinessCentralLineItem>
            {
                new("MITOQ-PURE-60", "MitoQ Pure Cellular Antioxidant 60 caps", 2, 89.975m, "NZD")
            },
            CorrelationId: "CORR-TEST-10091"
        );

        var response = await _client.PostAsJsonAsync("/api/erp/business-central/orders/sync", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BusinessCentralSyncResult>();
        Assert.NotNull(result);
        Assert.Equal("SHOPIFY-NZ-10091", result.ExternalReference);
        Assert.StartsWith("BC-ORD-", result.BusinessCentralDocumentNo);
        Assert.Equal("PostedToLedger", result.Status);
        Assert.Equal(179.95m, result.TotalAmount);
        Assert.Equal("CORR-TEST-10091", result.CorrelationId);
    }

    [Fact]
    public async Task SyncOrder_IsIdempotent_WhenSameExternalReferenceProvided()
    {
        var request1 = new BusinessCentralOrderSyncRequest(
            ExternalReference: "AMZ-US-99120",
            SalesChannel: "Amazon-US",
            CustomerEmail: "us-buyer@amazon.com",
            DestinationCountry: "US",
            TotalAmount: 125.00m,
            Currency: "USD",
            LineItems: new List<BusinessCentralLineItem>
            {
                new("MITOQ-HEART-60", "MitoQ Heart 60 caps", 1, 125.00m, "USD")
            }
        );

        var res1 = await _client.PostAsJsonAsync("/api/erp/business-central/orders/sync", request1);
        var doc1 = await res1.Content.ReadFromJsonAsync<BusinessCentralSyncResult>();

        var request2 = request1 with { TotalAmount = 999.00m }; // even with different payload, same ref returns original
        var res2 = await _client.PostAsJsonAsync("/api/erp/business-central/orders/sync", request2);
        var doc2 = await res2.Content.ReadFromJsonAsync<BusinessCentralSyncResult>();

        Assert.NotNull(doc1);
        Assert.NotNull(doc2);
        Assert.Equal(doc1.SyncId, doc2.SyncId);
        Assert.Equal(doc1.BusinessCentralDocumentNo, doc2.BusinessCentralDocumentNo);
        Assert.Equal(doc1.TotalAmount, doc2.TotalAmount);
    }

    [Fact]
    public async Task SyncOrder_Fails_WhenLineItemsEmpty()
    {
        var request = new BusinessCentralOrderSyncRequest(
            ExternalReference: "ERR-NO-ITEMS",
            SalesChannel: "ShopifyPlus-NZ",
            CustomerEmail: "bad@example.com",
            DestinationCountry: "NZ",
            TotalAmount: 0m,
            Currency: "NZD",
            LineItems: new List<BusinessCentralLineItem>()
        );

        var response = await _client.PostAsJsonAsync("/api/erp/business-central/orders/sync", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReconcileInventory_FlagsDiscrepancies_WhenVarianceDetected()
    {
        var batch = new InventoryReconciliationBatch(
            WarehouseId: "3PL-AUCKLAND-AIRPORT",
            WarehouseName: "Auckland International 3PL Hub",
            SnapshotUtc: DateTime.UtcNow,
            StockLevels: new List<InventoryWarehouseStock>
            {
                new("MITOQ-PURE-60", 1150, 20), // expected default 1200, variance -50
                new("MITOQ-HEART-60", 850, 10)  // expected 850, balanced
            }
        );

        var response = await _client.PostAsJsonAsync("/api/erp/business-central/inventory/reconcile", batch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<ReconciliationReport>();
        Assert.NotNull(report);
        Assert.Equal("DiscrepancyDetected", report.Status);
        Assert.Equal(1, report.DiscrepanciesCount);

        var item = report.Discrepancies.First();
        Assert.Equal("MITOQ-PURE-60", item.Sku);
        Assert.Equal(-50, item.Variance);
        Assert.Equal("InvestigateShrinkageOrMissingInbound", item.RecommendedAction);
    }

    [Fact]
    public void UnitService_ReconcileInventory_Balanced_WhenPhysicalMatchesErp()
    {
        var batch = new InventoryReconciliationBatch(
            WarehouseId: "3PL-MELBOURNE",
            WarehouseName: "Melbourne Distribution Centre",
            SnapshotUtc: DateTime.UtcNow,
            StockLevels: new List<InventoryWarehouseStock>
            {
                new("MITOQ-PURE-60", 1200, 0),
                new("MITOQ-HEART-60", 850, 0),
                new("MITOQ-BRAIN-60", 620, 0)
            }
        );

        var report = _unitService.ReconcileInventory(batch);
        Assert.Equal("Balanced", report.Status);
        Assert.Equal(0, report.DiscrepanciesCount);
        Assert.Empty(report.Discrepancies);
    }

    [Fact]
    public void UnitService_DeadLetterRecovery_RequeuesMessagesSuccessfully()
    {
        var request = new DeadLetterRecoveryRequest(
            SubscriptionName: "sb-sub-businesscentral-order-ingestion/$deadletterqueue",
            MaxBatchSize: 50
        );

        var result = _unitService.RecoverDeadLetters(request);
        Assert.Equal("sb-sub-businesscentral-order-ingestion/$deadletterqueue", result.SubscriptionName);
        Assert.Equal(22, result.RequeuedCount);
        Assert.Equal(3, result.PermanentlyQuarantinedCount);
    }
}
