namespace Merconiq.Core.Entities;

/// <summary>
/// Company-owned fiscal-year definition whose periods gate financial posting dates.
/// Years are configuration only; the posting gate lives on <see cref="FiscalPeriod"/>.
/// </summary>
public sealed class FiscalYear : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-supplied display name, for example "FY2026".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Inclusive first calendar day of the fiscal year.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive last calendar day of the fiscal year.</summary>
    public DateOnly EndDate { get; set; }

    public ICollection<FiscalPeriod> Periods { get; set; } = new List<FiscalPeriod>();
}
