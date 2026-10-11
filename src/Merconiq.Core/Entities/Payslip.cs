namespace Merconiq.Core.Entities;

/// <summary>
/// One employee's immutable payslip inside a posted pay run. Values snapshot the effective
/// salary structure; deductions can include advances repaid through the payroll.
/// </summary>
public sealed class Payslip : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PayRunId { get; set; }

    public PayRun PayRun { get; set; } = null!;

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    /// <summary>Base salary snapshot for the period.</summary>
    public decimal BaseSalary { get; set; }

    /// <summary>Allowances snapshot for the period.</summary>
    public decimal Allowances { get; set; }

    /// <summary>Deductions withheld for the period (for example salary advances repaid).</summary>
    public decimal Deductions { get; set; }

    /// <summary>Gross = base + allowances; Net = gross − deductions.</summary>
    public decimal Gross { get; set; }

    public decimal Net { get; set; }

    public string? Notes { get; set; }
}
