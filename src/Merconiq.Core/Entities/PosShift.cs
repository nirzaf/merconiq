namespace Merconiq.Core.Entities;

/// <summary>
/// One cashier shift on one register. Opening float and the closing cash count reconcile
/// against the theoretical cash derived from posted tenders; any variance posts a
/// cash-over/short journal exactly once at close.
/// </summary>
public sealed class PosShift : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int RegisterId { get; set; }

    public PosRegister Register { get; set; } = null!;

    public PosShiftStatus Status { get; set; } = PosShiftStatus.Open;

    /// <summary>Cash in the drawer at open.</summary>
    public decimal OpeningFloat { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    public string OpenedBy { get; set; } = string.Empty;

    public DateTime? ClosedAtUtc { get; set; }

    public string? ClosedBy { get; set; }

    /// <summary>Physically counted cash recorded at close.</summary>
    public decimal? CountedCash { get; set; }

    /// <summary>Theoretical cash at close: float plus cash tenders minus cash refunds.</summary>
    public decimal? ExpectedCash { get; set; }

    /// <summary>Counted minus expected; posted to the cash over/short account at close.</summary>
    public decimal? OverShortValue { get; set; }

    /// <summary>Reviewer who approved a pending close; the over/short journal posts at approval.</summary>
    public string? CloseApprovedBy { get; set; }

    public DateTime? CloseApprovedAtUtc { get; set; }

    /// <summary>Reviewer who sent a pending close back to the cashier for a recount.</summary>
    public string? CloseRejectedBy { get; set; }

    public DateTime? CloseRejectedAtUtc { get; set; }

    /// <summary>Mandatory reviewer reason recorded with a rejected close.</summary>
    public string? CloseRejectionReason { get; set; }
}
