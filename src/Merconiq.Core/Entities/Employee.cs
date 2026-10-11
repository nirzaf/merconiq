namespace Merconiq.Core.Entities;

/// <summary>One company employee. HR data is company-scoped; payroll references it.</summary>
public sealed class Employee : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-supplied employee code; unique per company.</summary>
    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? Position { get; set; }

    public DateOnly HiredOn { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional link to the employee's login account within the tenant.</summary>
    public string? UserId { get; set; }
}
