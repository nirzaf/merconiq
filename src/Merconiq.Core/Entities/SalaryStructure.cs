namespace Merconiq.Core.Entities;

/// <summary>
/// One employee's monthly salary structure, effective-dated. Payroll lines snapshot the
/// structure values so later edits never rewrite pay history.
/// </summary>
public sealed class SalaryStructure : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>First month (first of month) this structure applies to.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Monthly base salary in base currency.</summary>
    public decimal BaseSalary { get; set; }

    /// <summary>Fixed monthly allowances in base currency.</summary>
    public decimal Allowances { get; set; }

    public bool IsActive { get; set; } = true;
}
