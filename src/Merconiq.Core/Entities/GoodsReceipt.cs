namespace Merconiq.Core.Entities;

/// <summary>
/// One posted goods receipt against an approved purchase order. Receipts are immutable: a retry
/// of the same request replays the committed receipt, and corrections go through the procurement
/// return/credit-note workflows, never through edits. Accepted quantity enters valued stock and
/// accrues Goods-Received-Not-Invoiced; rejected quantity is recorded evidence only and never
/// enters sellable stock.
/// </summary>
public sealed class GoodsReceipt : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PurchaseOrderId { get; set; }

    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    /// <summary>Company location that received the goods.</summary>
    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    /// <summary>Calendar posting date; must fall inside an open fiscal period.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>Supplier-side delivery note reference, when supplied.</summary>
    public string? SupplierDeliveryReference { get; set; }

    public string? Notes { get; set; }

    /// <summary>Client idempotency key; one receipt per tenant/company/key.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the committed request payload; conflicting retries are rejected.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public DateTime ReceivedAtUtc { get; set; }

    public string ReceivedBy { get; set; } = string.Empty;

    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}
