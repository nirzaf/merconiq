namespace Merconiq.Core.Entities;

/// <summary>
/// One posted supplier invoice matched to posted goods-receipt lines. The invoice clears the
/// accrued Goods-Received-Not-Invoiced value and recognizes the accounts-payable liability; it
/// never receives stock again. Matched receipt lines are consumed exactly once, and a supplier
/// invoice number stays unique per company and supplier. Corrections are credit notes, never
/// edits.
/// </summary>
public sealed class PurchaseInvoice : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    /// <summary>The supplier-supplied invoice number; unique per company and supplier.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly InvoiceDate { get; set; }

    /// <summary>Agreed payment due date recorded from supplier terms.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Calendar posting date; must fall inside an open fiscal period.</summary>
    public DateOnly PostingDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Client idempotency key; one invoice per tenant/company/key.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the committed request payload.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total payable value, equal to the matched GRNI clearing amount.</summary>
    public decimal TotalValue { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<PurchaseInvoiceLine> Lines { get; set; } = new List<PurchaseInvoiceLine>();
}
