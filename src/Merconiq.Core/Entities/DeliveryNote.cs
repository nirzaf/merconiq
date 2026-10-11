namespace Merconiq.Core.Entities;

/// <summary>
/// One posted delivery against an approved sales order. The delivery issues stock through the
/// stock ledger at moving-average cost and records the issued stock transaction per line, which
/// is the exact source of the COGS journal. Deliveries are immutable.
/// </summary>
public sealed class DeliveryNote : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SalesOrderId { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;

    /// <summary>Company location the goods ship from.</summary>
    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string DeliveryNumber { get; set; } = string.Empty;

    public DateOnly PostingDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Client idempotency key; one delivery per tenant/company/key.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the committed request payload.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public DateTime DeliveredAtUtc { get; set; }

    public string DeliveredBy { get; set; } = string.Empty;

    public ICollection<DeliveryNoteLine> Lines { get; set; } = new List<DeliveryNoteLine>();
}
