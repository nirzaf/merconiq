namespace Merconiq.Core.Entities;

/// <summary>
/// One immutable settlement of payment money against one open document (a purchase invoice or a
/// sales invoice; exactly one target). Allocations are append-only evidence; outstanding balances
/// are derived from them, never stored as mutable counters.
/// </summary>
public sealed class PaymentAllocation : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PaymentEntryId { get; set; }

    /// <summary>One-based allocation ordinal inside the payment.</summary>
    public int LineNumber { get; set; }

    /// <summary>The purchase invoice settled, for supplier payments.</summary>
    public int? PurchaseInvoiceId { get; set; }

    public PurchaseInvoice? PurchaseInvoice { get; set; }

    /// <summary>The sales invoice settled, for customer receipts.</summary>
    public int? SalesInvoiceId { get; set; }

    public SalesInvoice? SalesInvoice { get; set; }

    /// <summary>Allocated amount in base currency; never negative.</summary>
    public decimal Amount { get; set; }

    public PaymentEntry PaymentEntry { get; set; } = null!;
}
