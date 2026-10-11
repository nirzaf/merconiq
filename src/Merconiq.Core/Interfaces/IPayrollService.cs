using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Payroll: effective-dated salary structures and monthly pay runs that post one immutable
/// journal (Dr salary expense / Cr bank / Cr deductions payable) through the finance kernel,
/// once per company month, with structure snapshots on every payslip.
/// </summary>
public interface IPayrollService
{
    Task<SalaryStructure> SetSalaryStructureAsync(
        SetSalaryStructureRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Drafts one pay run from each active employee's effective structure for the month.</summary>
    Task<PayRun> DraftPayRunAsync(int companyId, DateOnly period, string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Posts the draft run: one immutable journal, one run per company month.</summary>
    Task<PayRun> PostPayRunAsync(int companyId, int payRunId, string actor,
        CancellationToken cancellationToken = default);

    Task<PayRun?> GetPayRunAsync(int companyId, int payRunId, CancellationToken cancellationToken = default);
}
