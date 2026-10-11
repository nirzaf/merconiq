namespace Merconiq.Core.Entities;

/// <summary>
/// One posted sales invoice matched to posted delivery lines. The invoice recognizes revenue and
/// the receivable for the delivered quantities; it never issues stock again. Matched delivery
/// lines are consumed exactly once. Returns and credit notes are the correction path.
/// </summary>
public sealed class SalesInvoice : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    /// <summary>Company-supplied invoice number; unique per company.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly InvoiceDate { get; set; }

    public DateOnly DueDate { get; set; }

    public DateOnly PostingDate { get; set; }

    public string? Notes { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total revenue value recognized on this invoice.</summary>
    public decimal TotalValue { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    /// <summary>Reviewer's reason when this invoice exceeded the customer's credit limit and was approved anyway.</summary>
    public string? CreditLimitOverrideReason { get; set; }

    /// <summary>The approver who recorded the credit-limit override; null when no override was needed.</summary>
    public string? CreditLimitOverrideBy { get; set; }

    public ICollection<SalesInvoiceLine> Lines { get; set; } = new List<SalesInvoiceLine>();
}
