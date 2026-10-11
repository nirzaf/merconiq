using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Fiscal-calendar controls that gate financial posting dates. Period state changes and journal
/// posting share one advisory-lock family per company so a period cannot close while a posting
/// is validating its date, and vice versa.
/// </summary>
public sealed class FiscalCalendarService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<FiscalCalendarService> logger) : IFiscalCalendarService
{
    private const int MaximumFiscalYearMonths = 24;
    private const int AdjustmentPeriodNumber = 13;

    /// <inheritdoc />
    public async Task<FiscalYear> CreateFiscalYearAsync(
        CreateFiscalYearRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateFiscalYearRequest(request);

        FiscalYear created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-calendar:{request.CompanyId}", cancellationToken);

            var company = await context.Companies.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CompanyId && candidate.IsActive, cancellationToken);
            if (company is null)
                throw new FinancePostingValidationException("Active company not found.");

            var years = await context.FiscalYears
                .Where(year => year.CompanyId == request.CompanyId)
                .ToListAsync(cancellationToken);
            foreach (var existing in years)
            {
                if (request.StartDate <= existing.EndDate && existing.StartDate <= request.EndDate)
                    throw new FinancePostingValidationException(
                        $"The requested fiscal-year range overlaps fiscal year '{existing.Name}'.");
            }

            var year = new FiscalYear
            {
                CompanyId = request.CompanyId,
                Name = request.Name.Trim(),
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Periods = BuildMonthlyPeriods(request)
            };
            context.FiscalYears.Add(year);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = year;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Created fiscal year {FiscalYear} for company {CompanyId} with {PeriodCount} periods",
            created.Name, request.CompanyId, created.Periods.Count);
        return created;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriod> ClosePeriodAsync(
        int companyId,
        int periodId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        FiscalPeriod closed = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Shares the lock family with journal posting: a close cannot commit while a
            // posting is validating its date inside the same period.
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-period:{companyId}", cancellationToken);

            var period = await FindCompanyPeriodAsync(companyId, periodId, cancellationToken);
            if (period.Status == FiscalPeriodStatus.Closed)
                throw new FinancePostingConflictException("The fiscal period is already closed.");

            period.Status = FiscalPeriodStatus.Closed;
            period.ClosedAtUtc = DateTime.UtcNow;
            period.ClosedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            closed = period;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Closed fiscal period {PeriodId} of company {CompanyId}", periodId, companyId);
        return closed;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriod> ReopenPeriodAsync(
        int companyId,
        int periodId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var normalizedReason = RequireReason(reason);
        FiscalPeriod reopened = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-period:{companyId}", cancellationToken);

            var period = await FindCompanyPeriodAsync(companyId, periodId, cancellationToken);
            if (period.Status == FiscalPeriodStatus.Open)
                throw new FinancePostingConflictException("The fiscal period is already open.");

            period.Status = FiscalPeriodStatus.Open;
            period.ReopenedAtUtc = DateTime.UtcNow;
            period.ReopenedBy = actor.Trim();
            period.ReopenReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            reopened = period;
        }, cancellationToken: cancellationToken);

        logger.LogWarning(
            "Reopened closed fiscal period {PeriodId} of company {CompanyId}; reopen reason recorded",
            periodId, companyId);
        return reopened;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FiscalYear>> ListFiscalYearsAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var years = await context.FiscalYears
            .Include(year => year.Periods)
            .Where(year => year.CompanyId == companyId)
            .OrderBy(year => year.StartDate)
            .ThenBy(year => year.Id)
            .ToListAsync(cancellationToken);
        return years;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriod> EnsurePeriodOpenForDateAsync(
        int companyId,
        DateOnly postingDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        // Runs inside the caller's posting transaction; the shared finance-period advisory lock
        // taken by both paths prevents a concurrent close from racing this validation.
        // Adjustment periods share the fiscal year's last day, so date mapping must skip them.
        var period = await context.FiscalPeriods.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == companyId &&
                !candidate.IsAdjustment &&
                candidate.StartDate <= postingDate &&
                candidate.EndDate >= postingDate,
            cancellationToken);

        if (period is null)
            throw new FinancePostingValidationException(
                $"No fiscal period covers the posting date {postingDate:yyyy-MM-dd} for this company.");
        if (period.Status == FiscalPeriodStatus.Closed)
            throw new FinancePostingValidationException(
                $"Fiscal period '{period.Name}' covering {postingDate:yyyy-MM-dd} is closed to posting.");

        return period;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriod> CreateAdjustmentPeriodAsync(
        int companyId,
        int fiscalYearId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        FiscalPeriod created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-calendar:{companyId}", cancellationToken);

            var year = await context.FiscalYears
                .Include(candidate => candidate.Periods)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == fiscalYearId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Fiscal year not found in this company.");
            if (year.Periods.Any(period => period.IsAdjustment))
                throw new FinancePostingConflictException("This fiscal year already has an adjustment period.");

            // Period start dates are unique per company; the year-end day must not already start a period.
            var startsOnYearEnd = await context.FiscalPeriods.AnyAsync(
                period => period.CompanyId == companyId && period.StartDate == year.EndDate,
                cancellationToken);
            if (startsOnYearEnd)
                throw new FinancePostingValidationException(
                    $"Another fiscal period starts on {year.EndDate:yyyy-MM-dd}, so the adjustment period cannot use the fiscal year end.");

            var name = $"{year.Name} P{AdjustmentPeriodNumber} Adjustments";
            var period = new FiscalPeriod
            {
                CompanyId = companyId,
                FiscalYearId = year.Id,
                PeriodNumber = AdjustmentPeriodNumber,
                Name = name.Length > 100 ? name[..100].TrimEnd() : name,
                StartDate = year.EndDate,
                EndDate = year.EndDate,
                Status = FiscalPeriodStatus.Open,
                IsAdjustment = true
            };
            context.FiscalPeriods.Add(period);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = period;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Created adjustment period for fiscal year {FiscalYearId} of company {CompanyId}",
            fiscalYearId, companyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriod> EnsureAdjustmentPeriodOpenAsync(
        int companyId,
        int adjustmentPeriodId,
        DateOnly postingDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var period = await FindCompanyPeriodAsync(companyId, adjustmentPeriodId, cancellationToken);
        if (!period.IsAdjustment)
            throw new FinancePostingValidationException(
                $"Fiscal period '{period.Name}' is not an adjustment period; post by its posting date instead.");
        if (postingDate != period.StartDate)
            throw new FinancePostingValidationException(
                $"Adjustment period '{period.Name}' accepts postings only on the fiscal year end {period.StartDate:yyyy-MM-dd}.");
        if (period.Status == FiscalPeriodStatus.Closed)
            throw new FinancePostingValidationException(
                $"Fiscal period '{period.Name}' is closed to posting.");

        return period;
    }

    private async Task<FiscalPeriod> FindCompanyPeriodAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken)
    {
        var period = await context.FiscalPeriods.SingleOrDefaultAsync(
            candidate => candidate.Id == periodId && candidate.CompanyId == companyId,
            cancellationToken);
        if (period is null)
            throw new FinancePostingValidationException("Fiscal period not found in this company.");
        return period;
    }

    private static void ValidateFiscalYearRequest(CreateFiscalYearRequest request)
    {
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100)
            throw new FinancePostingValidationException("A fiscal-year name of 1 to 100 characters is required.");
        if (request.StartDate > request.EndDate)
            throw new FinancePostingValidationException("The fiscal year must end on or after its start date.");
        var months = (request.EndDate.Year - request.StartDate.Year) * 12
            + request.EndDate.Month - request.StartDate.Month;
        if (months >= MaximumFiscalYearMonths)
            throw new FinancePostingValidationException(
                $"A fiscal year may span at most {MaximumFiscalYearMonths} months.");
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reopen reason of 1 to 500 characters is required.");
        return trimmed;
    }

    private static List<FiscalPeriod> BuildMonthlyPeriods(CreateFiscalYearRequest request)
    {
        var periods = new List<FiscalPeriod>();
        var cursor = request.StartDate;
        var number = 1;
        while (cursor <= request.EndDate)
        {
            var endOfMonth = new DateOnly(
                cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var periodEnd = endOfMonth < request.EndDate ? endOfMonth : request.EndDate;
            periods.Add(new FiscalPeriod
            {
                PeriodNumber = number,
                Name = $"{request.Name.Trim()} P{number:00}",
                StartDate = cursor,
                EndDate = periodEnd,
                Status = FiscalPeriodStatus.Open
            });
            cursor = periodEnd.AddDays(1);
            number++;
        }

        return periods;
    }
}
