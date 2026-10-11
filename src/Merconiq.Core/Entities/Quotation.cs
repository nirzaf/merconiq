namespace Merconiq.Core.Entities;

/// <summary>
/// One company quotation to a customer: a priced offer with a validity window. Quotations never
/// post stock or finance; conversion consumes the quotation once into a sales order whose lines
/// are bounded by the quoted quantities. Prices are snapshotted at creation.
/// </summary>
public sealed class Quotation : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string QuotationNumber { get; set; } = string.Empty;

    public QuotationStatus Status { get; set; } = QuotationStatus.Open;

    public DateOnly QuotationDate { get; set; }

    /// <summary>Last date on which the offer can be accepted.</summary>
    public DateOnly ValidUntil { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? AcceptedAtUtc { get; set; }

    /// <summary>The sales order this quotation was converted into, once accepted.</summary>
    public int? SalesOrderId { get; set; }

    public ICollection<QuotationLine> Lines { get; set; } = new List<QuotationLine>();
}
