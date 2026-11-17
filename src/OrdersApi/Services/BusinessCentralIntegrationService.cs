using System.Collections.Concurrent;
using OrdersApi.Models;

namespace OrdersApi.Services;

public sealed class BusinessCentralIntegrationService
{
    private readonly ConcurrentDictionary<string, BusinessCentralSyncResult> _syncedOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ReconciliationReport> _reconciliations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _erpInventoryLedger = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MITOQ-PURE-60"] = 1200,
        ["MITOQ-HEART-60"] = 850,
        ["MITOQ-BRAIN-60"] = 620,
        ["MITOQ-CURCUMIN-60"] = 450,
        ["MITOQ-JOINT-60"] = 710
    };

    public BusinessCentralSyncResult SyncOrder(BusinessCentralOrderSyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ExternalReference))
            throw new ArgumentException("ExternalReference must not be empty.", nameof(request));

        if (request.LineItems == null || request.LineItems.Count == 0)
            throw new ArgumentException("LineItems cannot be empty for an ERP sync order.", nameof(request));

        // Idempotency: return existing sync result if already processed
        if (_syncedOrders.TryGetValue(request.ExternalReference, out var existing))
        {
            return existing;
        }

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"CORR-{Guid.NewGuid():N}"
            : request.CorrelationId;

        var syncId = $"BC-SYNC-{Guid.NewGuid():N}";
        var docNo = $"BC-ORD-{DateTime.UtcNow:yyyyMMdd}-{Math.Abs(request.ExternalReference.GetHashCode()) % 1000000:D6}";

        var result = new BusinessCentralSyncResult(
            SyncId: syncId,
            ExternalReference: request.ExternalReference,
            BusinessCentralDocumentNo: docNo,
            Status: "PostedToLedger",
            TotalAmount: request.TotalAmount,
            Currency: request.Currency,
            SyncedAtUtc: DateTime.UtcNow,
            CorrelationId: correlationId
        );

        _syncedOrders[request.ExternalReference] = result;

        // Deduct inventory from internal ERP ledger
        foreach (var item in request.LineItems)
        {
            _erpInventoryLedger.AddOrUpdate(
                item.Sku,
                _ => 0,
                (_, current) => Math.Max(0, current - item.Quantity)
            );
        }

        return result;
    }

    public BusinessCentralSyncResult? GetOrderByExternalRef(string externalReference)
    {
        _syncedOrders.TryGetValue(externalReference, out var result);
        return result;
    }

    public ReconciliationReport ReconcileInventory(InventoryReconciliationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var batchId = $"REC-{Guid.NewGuid():N}";
        var discrepancies = new List<ReconciliationDiscrepancy>();

        foreach (var stock in batch.StockLevels)
        {
            var erpQty = _erpInventoryLedger.TryGetValue(stock.Sku, out var qty) ? qty : 0;
            var variance = stock.PhysicalStockOnHand - erpQty;

            if (variance != 0)
            {
                var action = variance > 0
                    ? "AdjustLedgerPositiveSurplus"
                    : "InvestigateShrinkageOrMissingInbound";

                discrepancies.Add(new ReconciliationDiscrepancy(
                    Sku: stock.Sku,
                    ErpLedgerQty: erpQty,
                    PhysicalWarehouseQty: stock.PhysicalStockOnHand,
                    Variance: variance,
                    RecommendedAction: action
                ));
            }
        }

        var status = discrepancies.Count == 0 ? "Balanced" : "DiscrepancyDetected";

        var report = new ReconciliationReport(
            BatchId: batchId,
            WarehouseId: batch.WarehouseId,
            TotalItemsAudited: batch.StockLevels.Count,
            DiscrepanciesCount: discrepancies.Count,
            Discrepancies: discrepancies,
            AuditedAtUtc: DateTime.UtcNow,
            Status: status
        );

        _reconciliations[batchId] = report;
        return report;
    }

    public ReconciliationReport? GetReconciliation(string batchId)
    {
        _reconciliations.TryGetValue(batchId, out var report);
        return report;
    }

    public DeadLetterRecoveryResult RecoverDeadLetters(DeadLetterRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Simulation of dead-letter recovery logic
        int scanned = Math.Min(request.MaxBatchSize, 25);
        int requeued = 22;
        int quarantined = 3;

        return new DeadLetterRecoveryResult(
            SubscriptionName: request.SubscriptionName,
            ScannedCount: scanned,
            RequeuedCount: requeued,
            PermanentlyQuarantinedCount: quarantined,
            CompletedAtUtc: DateTime.UtcNow
        );
    }
}
