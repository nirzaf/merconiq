namespace Merconiq.Core.Entities;

/// <summary>
/// One payroll run for one company month. Posting commits one immutable journal — Dr salary
/// expense (gross) / Cr bank (net) / Cr deductions payable — through the finance kernel, once
/// per run; one run per company month; payslips snapshot the structures at posting time.
/// </summary>
public sealed class PayRun : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Payroll month, always normalized to the first of the month.</summary>
    public DateOnly Period { get; set; }

    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;

    /// <summary>Total gross across payslips at posting time.</summary>
    public decimal TotalGross { get; set; }

    /// <summary>Total deductions withheld at posting time.</summary>
    public decimal TotalDeductions { get; set; }

    /// <summary>Total net paid out at posting time.</summary>
    public decimal TotalNet { get; set; }

    public string RequestKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
}
