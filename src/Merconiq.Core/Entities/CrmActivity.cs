namespace Merconiq.Core.Entities;

/// <summary>
/// A logged interaction on exactly one lead or one opportunity. Activities give the pipeline its history and
/// never change money or stock.
/// </summary>
public sealed class CrmActivity : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? LeadId { get; set; }

    public int? OpportunityId { get; set; }

    public CrmActivityKind Kind { get; set; }

    public string Summary { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string RecordedByUser { get; set; } = string.Empty;
}
