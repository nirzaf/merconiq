namespace Merconiq.Core.Entities;

/// <summary>
/// One posted supplier credit note returning goods against a posted goods-receipt line. Bound by
/// the accepted-minus-returned quantity; issues stock out through the stock ledger and posts
/// Dr AP / Cr Inventory for the returned value. A receipt line already matched by an invoice
/// cannot be credit-noted in this slice (invoice credit notes are a future increment).
/// </summary>
public sealed class PurchaseCreditNote : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SupplierId { get; set; }

    public Supplier Supplier { get; set; } = null!;

    /// <summary>Supplier-supplied credit-note number; unique per company and supplier.</summary>
    public string CreditNoteNumber { get; set; } = string.Empty;

    public DateOnly PostingDate { get; set; }

    /// <summary>Mandatory reviewer-visible reason.</summary>
    public string Reason { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total credited value, equal to the stock value issued back to the supplier.</summary>
    public decimal TotalValue { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<PurchaseCreditNoteLine> Lines { get; set; } = new List<PurchaseCreditNoteLine>();
}
