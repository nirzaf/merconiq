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

    /// <summary>
    /// Creates the year-end adjustment period (period 13) for one fiscal year. Only one adjustment period
    /// may exist per year, and it covers the year's last day without claiming that day from the regular period.
    /// </summary>
    Task<FiscalPeriod> CreateAdjustmentPeriodAsync(
        int companyId,
        int fiscalYearId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the open adjustment period when a journal explicitly targets it and its posting date is the
    /// fiscal year end; throws <see cref="Exceptions.FinancePostingValidationException"/> otherwise.
    /// Runs inside the posting transaction, like <see cref="EnsurePeriodOpenForDateAsync"/>.
    /// </summary>
    Task<FiscalPeriod> EnsureAdjustmentPeriodOpenAsync(
        int companyId,
        int adjustmentPeriodId,
        DateOnly postingDate,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the company's fiscal years with their ordered periods.</summary>
    Task<IReadOnlyList<FiscalYear>> ListFiscalYearsAsync(
        int companyId,
        CancellationToken cancellationToken = default);
}
