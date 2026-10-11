namespace Merconiq.Core.Entities;

/// <summary>
/// One posted point-of-sale checkout. The sale issues valued stock per line, records the tender
/// split, and posts the revenue and COGS journals atomically, exactly once per request key.
/// Sales require an open shift on their register; refunds reference the original sale.
/// </summary>
public sealed class PosSale : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int RegisterId { get; set; }

    public PosRegister Register { get; set; } = null!;

    public int ShiftId { get; set; }

    public PosShift Shift { get; set; } = null!;

    /// <summary>Optional walk-in customer attribution.</summary>
    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Total charged across all tenders.</summary>
    public decimal TotalValue { get; set; }

    public DateOnly PostingDate { get; set; }

    public DateTime SoldAtUtc { get; set; }

    public string SoldBy { get; set; } = string.Empty;

    public ICollection<PosSaleLine> Lines { get; set; } = new List<PosSaleLine>();

    public ICollection<PosSaleTender> Tenders { get; set; } = new List<PosSaleTender>();
}
