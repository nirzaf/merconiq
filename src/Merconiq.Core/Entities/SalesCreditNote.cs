namespace Merconiq.Core.Entities;

/// <summary>
/// One posted sales credit note returning goods against a posted delivery line. Bound by the
/// delivered-minus-returned quantity; stock returns through the stock ledger with an operational
/// disposition, revenue and receivable reverse, and restocked value reverses COGS.
/// </summary>
public sealed class SalesCreditNote : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    /// <summary>Company-supplied credit-note number; unique per company.</summary>
    public string CreditNoteNumber { get; set; } = string.Empty;

    public DateOnly PostingDate { get; set; }

    /// <summary>Mandatory reviewer-visible reason.</summary>
    public string Reason { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total credited revenue value.</summary>
    public decimal TotalValue { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<SalesCreditNoteLine> Lines { get; set; } = new List<SalesCreditNoteLine>();
}
