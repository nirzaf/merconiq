namespace Merconiq.Core.Entities;

/// <summary>A prospective customer captured before any commercial commitment. Leads never post to the ledger.</summary>
public sealed class CrmLead : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string Source { get; set; } = string.Empty;

    public CrmLeadStatus Status { get; set; } = CrmLeadStatus.New;

    public string? DisqualifiedReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedByUser { get; set; } = string.Empty;
}
