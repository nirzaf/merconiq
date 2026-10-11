using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Payroll runs. Drafting snapshots each active employee's effective salary structure for the
/// period; posting commits one immutable journal (Dr salary expense / Cr bank / Cr deductions
/// payable) through the finance kernel, exactly once per run, and one run per company month is
/// enforced by a unique index. Payslip values are snapshots — later structure edits never
/// rewrite pay history.
/// </summary>
public sealed class PayrollService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    ILogger<PayrollService> logger) : IPayrollService
{
    /// <summary>Unique pay-run period index; duplicate runs classify against this name.</summary>
    public const string PeriodIndex = "UX_PayRuns_Period";

    /// <summary>Request-key index for replayed postings.</summary>
    public const string RequestKeyIndex = "UX_PayRuns_RequestKey";

    /// <inheritdoc />
    public async Task<SalaryStructure> SetSalaryStructureAsync(
        SetSalaryStructureRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.BaseSalary < 0 || request.Allowances < 0)
            throw new FinancePostingValidationException("Salary components must not be negative.");
        if (request.EffectiveFrom != new DateOnly(request.EffectiveFrom.Year, request.EffectiveFrom.Month, 1))
            throw new FinancePostingValidationException(
                "The effective date must be the first day of a month.");
        var scale = (await RequireCompanyAsync(request.CompanyId, cancellationToken)).CurrencyScale ?? 2;

        SalaryStructure saved = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"payroll:{request.CompanyId}", cancellationToken);
            var employee = await context.Employees.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.EmployeeId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Employee not found in this company.");

            var structure = await context.SalaryStructures.SingleOrDefaultAsync(
                candidate => candidate.CompanyId == request.CompanyId &&
                    candidate.EmployeeId == request.EmployeeId &&
                    candidate.EffectiveFrom == request.EffectiveFrom,
                cancellationToken);
            if (structure is null)
            {
                structure = new SalaryStructure
                {
                    CompanyId = request.CompanyId,
                    EmployeeId = employee.Id,
                    EffectiveFrom = request.EffectiveFrom
                };
                context.SalaryStructures.Add(structure);
            }

            structure.BaseSalary = decimal.Round(request.BaseSalary, scale, MidpointRounding.AwayFromZero);
            structure.Allowances = decimal.Round(request.Allowances, scale, MidpointRounding.AwayFromZero);
            structure.IsActive = true;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            saved = structure;
        }, cancellationToken: cancellationToken);
        return saved;
    }

    /// <inheritdoc />
    public async Task<PayRun> DraftPayRunAsync(
        int companyId, DateOnly period, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var normalizedPeriod = new DateOnly(period.Year, period.Month, 1);
        PayRun created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"payroll:{companyId}", cancellationToken);
            await RequireCompanyAsync(companyId, cancellationToken);
            if (await context.PayRuns.AnyAsync(
                    run => run.CompanyId == companyId && run.Period == normalizedPeriod, cancellationToken))
                throw new FinancePostingConflictException(
                    $"A pay run for {normalizedPeriod:yyyy-MM} already exists in this company.");

            var monthEnd = normalizedPeriod.AddMonths(1).AddDays(-1);
            var employees = await context.Employees
                .Where(employee => employee.CompanyId == companyId && employee.IsActive)
                .ToListAsync(cancellationToken);
            if (employees.Count == 0)
                throw new FinancePostingValidationException("The company has no active employees to pay.");

            var run = new PayRun
            {
                CompanyId = companyId,
                Period = normalizedPeriod,
                Status = PayRunStatus.Draft,
                RequestKey = $"payrun-draft:{companyId}:{normalizedPeriod:yyyy-MM}"
            };
            foreach (var employee in employees)
            {
                var structure = await context.SalaryStructures
                    .Where(candidate => candidate.CompanyId == companyId &&
                        candidate.EmployeeId == employee.Id &&
                        candidate.IsActive &&
                        candidate.EffectiveFrom <= monthEnd)
                    .OrderByDescending(candidate => candidate.EffectiveFrom)
                    .FirstOrDefaultAsync(cancellationToken);
                if (structure is null)
                {
                    // Employees without a structure are skipped and surfaced in the run notes
                    // rather than silently underpaid.
                    run.Payslips.Add(new Payslip
                    {
                        CompanyId = companyId,
                        EmployeeId = employee.Id,
                        BaseSalary = 0m,
                        Allowances = 0m,
                        Deductions = 0m,
                        Gross = 0m,
                        Net = 0m,
                        Notes = "No effective salary structure; skipped at posting."
                    });
                    continue;
                }

                var gross = structure.BaseSalary + structure.Allowances;
                run.Payslips.Add(new Payslip
                {
                    CompanyId = companyId,
                    EmployeeId = employee.Id,
                    BaseSalary = structure.BaseSalary,
                    Allowances = structure.Allowances,
                    Deductions = 0m,
                    Gross = gross,
                    Net = gross,
                    Notes = null
                });
            }

            context.PayRuns.Add(run);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = run;
        }, cancellationToken: cancellationToken);
        logger.LogInformation(
            "Pay run drafted for {Period:yyyy-MM} in company {CompanyId}: {SlipCount} payslips",
            normalizedPeriod, companyId, created.Payslips.Count);
        return created;
    }

    /// <inheritdoc />
    public async Task<PayRun> PostPayRunAsync(
        int companyId, int payRunId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        PayRun posted = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync($"payroll:{companyId}", cancellationToken);
                var run = await context.PayRuns
                    .Include(candidate => candidate.Payslips)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == payRunId && candidate.CompanyId == companyId,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Pay run not found in this company.");

                if (run.Status == PayRunStatus.Posted)
                {
                    // Idempotent: the committed journal answers repeated postings.
                    posted = run;
                    return;
                }

                var company = await RequireCompanyAsync(companyId, cancellationToken);
                var scale = company.CurrencyScale ?? 2;
                var payableSlips = run.Payslips.Where(slip => slip.Gross > 0).ToList();
                if (payableSlips.Count == 0)
                    throw new FinancePostingValidationException(
                        "The pay run has no payable payslips; every employee lacks a salary structure.");

                var period = new DateOnly(run.Period.Year, run.Period.Month, 1);
                await fiscalCalendar.EnsurePeriodOpenForDateAsync(companyId, period, cancellationToken);

                run.TotalGross = payableSlips.Sum(slip => slip.Gross);
                run.TotalDeductions = payableSlips.Sum(slip => slip.Deductions);
                run.TotalNet = run.TotalGross - run.TotalDeductions;
                run.RequestKey = $"payrun:{companyId}:{run.Period:yyyy-MM}";
                run.RequestHash = ComputeHash(run);

                var expenseAccount = await postingPolicy.ResolveAsync(
                    companyId, AccountPostingRole.SalaryExpense, cancellationToken);
                var bankAccount = await postingPolicy.ResolveAsync(
                    companyId, AccountPostingRole.Bank, cancellationToken);

                var lines = new List<JournalLineInput>
                {
                    new(expenseAccount.ChartOfAccountId, $"Salaries {run.Period:yyyy-MM}", run.TotalGross, 0m)
                };
                if (run.TotalDeductions > 0)
                {
                    var deductionsAccount = await postingPolicy.ResolveAsync(
                        companyId, AccountPostingRole.DeductionsPayable, cancellationToken);
                    lines.Add(new JournalLineInput(
                        deductionsAccount.ChartOfAccountId, $"Deductions withheld {run.Period:yyyy-MM}", 0m, run.TotalDeductions));
                }
                lines.Add(new JournalLineInput(
                    bankAccount.ChartOfAccountId, $"Net salaries paid {run.Period:yyyy-MM}", 0m, run.TotalNet));

                await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        companyId,
                        period,
                        company.BaseCurrency,
                        $"Payroll for {run.Period:yyyy-MM}",
                        "PayRun",
                        run.Id.ToString(CultureInfo.InvariantCulture),
                        "Post",
                        1,
                        lines),
                    actor,
                    cancellationToken);

                run.Status = PayRunStatus.Posted;
                run.PostedAtUtc = DateTime.UtcNow;
                run.PostedBy = actor.Trim();
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
                posted = run;
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.PayRuns.AnyAsync(
                run => run.Id == payRunId && run.Status == PayRunStatus.Posted, cancellationToken));

        logger.LogInformation(
            "Pay run {PayRunId} posted for company {CompanyId}: gross {Gross}, deductions {Deductions}, net {Net}",
            posted.Id, companyId, posted.TotalGross, posted.TotalDeductions, posted.TotalNet);
        return posted;
    }

    /// <inheritdoc />
    public Task<PayRun?> GetPayRunAsync(
        int companyId, int payRunId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.PayRuns
            .Include(run => run.Payslips.OrderBy(slip => slip.EmployeeId))
            .SingleOrDefaultAsync(run => run.Id == payRunId && run.CompanyId == companyId, cancellationToken);
    }

    private Task<Company> RequireCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        context.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private static string ComputeHash(PayRun run)
    {
        var canonical = new StringBuilder();
        canonical.Append("payrun\u001f").Append(run.CompanyId).Append('\u001f')
            .Append(run.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Append('\u001f')
            .Append(run.TotalGross.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f')
            .Append(run.TotalDeductions.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f')
            .Append(run.TotalNet.ToString("0.########", CultureInfo.InvariantCulture));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
