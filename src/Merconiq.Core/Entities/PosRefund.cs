namespace Merconiq.Core.Entities;

/// <summary>
/// One posted refund against a point-of-sale sale. Refund quantities are bounded by the
/// sold-minus-refunded balance per line, stock returns through the stock ledger, and the
/// revenue and stock journals post once per request key. Refunds cannot be refunded.
/// </summary>
public sealed class PosRefund : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int RegisterId { get; set; }

    public int ShiftId { get; set; }

    public int OriginalPosSaleId { get; set; }

    public PosSale OriginalPosSale { get; set; } = null!;

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total money returned to the customer.</summary>
    public decimal TotalValue { get; set; }

    public DateOnly PostingDate { get; set; }

    public DateTime RefundedAtUtc { get; set; }

    public string RefundedBy { get; set; } = string.Empty;

    /// <summary>Mandatory reviewer-visible reason recorded on the refund.</summary>
    public string Reason { get; set; } = string.Empty;

    public ICollection<PosRefundLine> Lines { get; set; } = new List<PosRefundLine>();
}
