namespace Merconiq.Core.Entities;

/// <summary>
/// One company project: the container for tasks and approved time. Projects never post to the
/// ledger by themselves; profitability reads derive from approved timesheet lines.
/// </summary>
public sealed class Project : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-supplied project code; unique per company.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional customer this project bills to.</summary>
    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public string? Notes { get; set; }
}
