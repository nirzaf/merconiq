using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>Owns fiscal-year and fiscal-period controls that gate financial posting dates.</summary>
public interface IFiscalCalendarService
{
    /// <summary>
    /// Creates one fiscal year and its monthly periods. The supplied date range must not overlap
    /// another fiscal year of the same company.
    /// </summary>
    Task<FiscalYear> CreateFiscalYearAsync(
        CreateFiscalYearRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Closes one open period. Closing is audited and final until an authorized reopen.</summary>
    Task<FiscalPeriod> ClosePeriodAsync(
        int companyId,
        int periodId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Reopens one closed period with a mandatory recorded reason.</summary>
    Task<FiscalPeriod> ReopenPeriodAsync(
        int companyId,
        int periodId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the open period covering the posting date, or throws a
    /// <see cref="Exceptions.FinancePostingValidationException"/> when no open period covers it.
    /// Callers must run this inside the posting transaction so a concurrent close cannot race it.
    /// </summary>
    Task<FiscalPeriod> EnsurePeriodOpenForDateAsync(
        int companyId,
        DateOnly postingDate,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the company's fiscal years with their ordered periods.</summary>
    Task<IReadOnlyList<FiscalYear>> ListFiscalYearsAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
