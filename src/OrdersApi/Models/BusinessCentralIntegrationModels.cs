namespace OrdersApi.Models;

public sealed record BusinessCentralLineItem(
    string Sku,
    string Description,
    int Quantity,
    decimal UnitPrice,
    string Currency = "NZD"
);

public sealed record BusinessCentralOrderSyncRequest(
    string ExternalReference,
    string SalesChannel,
    string CustomerEmail,
    string DestinationCountry,
    decimal TotalAmount,
    string Currency,
    List<BusinessCentralLineItem> LineItems,
    string? CorrelationId = null
);

public sealed record BusinessCentralSyncResult(
    string SyncId,
    string ExternalReference,
    string BusinessCentralDocumentNo,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTime SyncedAtUtc,
    string CorrelationId
);

public sealed record InventoryWarehouseStock(
    string Sku,
    int PhysicalStockOnHand,
    int AllocatedQuantity
);

public sealed record InventoryReconciliationBatch(
    string WarehouseId,
    string WarehouseName,
    DateTime SnapshotUtc,
    List<InventoryWarehouseStock> StockLevels
);

public sealed record ReconciliationDiscrepancy(
    string Sku,
    int ErpLedgerQty,
    int PhysicalWarehouseQty,
    int Variance,
    string RecommendedAction
);

public sealed record ReconciliationReport(
    string BatchId,
    string WarehouseId,
    int TotalItemsAudited,
    int DiscrepanciesCount,
    List<ReconciliationDiscrepancy> Discrepancies,
    DateTime AuditedAtUtc,
    string Status
);

public sealed record DeadLetterRecoveryRequest(
    string SubscriptionName,
    int MaxBatchSize = 50,
    int MaxRetryThreshold = 3
);

public sealed record DeadLetterRecoveryResult(
    string SubscriptionName,
    int ScannedCount,
    int RequeuedCount,
    int PermanentlyQuarantinedCount,
    DateTime CompletedAtUtc
);
