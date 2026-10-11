namespace Merconiq.Core.Entities;

/// <summary>One task inside a project; time entries reference it.</summary>
public sealed class ProjectTask : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Open;
}
