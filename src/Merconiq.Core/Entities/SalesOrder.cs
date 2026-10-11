namespace Merconiq.Core.Entities;

/// <summary>
/// One approved sales order for a company customer. The order is a commercial commitment only:
/// it never posts stock or finance. Delivery evidence and invoicing derive from posted source
/// links, and the ordered-line outstanding quantity is computed from them.
/// </summary>
public sealed class SalesOrder : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Client idempotency key; one order per tenant/company/key.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the committed request payload.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public DateOnly OrderDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Currency scale used for the line amount snapshots.</summary>
    public int CurrencyScale { get; set; } = 2;

    public DateTime ApprovedAtUtc { get; set; }

    public string ApprovedBy { get; set; } = string.Empty;

    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
}
