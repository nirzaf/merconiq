namespace Merconiq.Core.Entities;

/// <summary>A company till bound to one stock location; sales settle against its cash account.</summary>
public sealed class PosRegister : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Largest cash variance (absolute, base currency) a shift may close with without review. Null means every
    /// close posts immediately, as before; a larger variance waits for an approver.
    /// </summary>
    public decimal? VarianceApprovalThreshold { get; set; }
}
