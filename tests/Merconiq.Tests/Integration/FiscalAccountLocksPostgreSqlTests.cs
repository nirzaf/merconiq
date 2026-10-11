using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for per-period account posting locks: a locked account rejects every journal
/// that touches it inside that fiscal period (reversals included), other periods and accounts stay open,
/// and lock administration rejects group, foreign-company and conflicting requests.
/// Requires real PostgreSQL semantics and is skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class FiscalAccountLocksPostgreSqlTests
{
    private const string Actor = "account-lock-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public FiscalAccountLocksPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task LockedAccount_RejectsJournalsInThatPeriodButNotOtherAccountsOrPeriods()
    {
        var tenantId = $"lock-{Guid.NewGuid():N}";
        var (companyId, accounts) = await CreateCompanyWithChartAsync(tenantId, "MAIN");
        var lockedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var otherPeriodDate = OtherMonthDate(lockedDate);
        var lockedPeriodId = await GetPeriodCoveringAsync(tenantId, companyId, lockedDate);

        await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, lockedPeriodId, accounts["1000"], "Bank reconciled through month end", Actor));

        var touchingBank = Post(companyId, lockedDate, "Bank deposit", accounts["1000"], accounts["3000"], 100m);
        var rejected = async () => await WithPostingAsync(tenantId, service => service.PostAsync(touchingBank, Actor));
        (await rejected.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*1000*locked*");

        var notTouchingBank = Post(companyId, lockedDate, "Equity only", accounts["2000"], accounts["3000"], 75m);
        var allowed = await WithPostingAsync(tenantId, service => service.PostAsync(notTouchingBank, Actor));
        allowed.Replayed.Should().BeFalse();

        var nextPeriod = Post(companyId, otherPeriodDate, "Other period bank", accounts["1000"], accounts["3000"], 40m);
        var otherPeriod = await WithPostingAsync(tenantId, service => service.PostAsync(nextPeriod, Actor));
        otherPeriod.Replayed.Should().BeFalse("the lock is scoped to one fiscal period");
    }

    [PostgreSqlFact]
    public async Task ReleasedLock_RestoresPostingsAndKeepsTheReleaseReasonOnRecord()
    {
        var tenantId = $"lock-{Guid.NewGuid():N}";
        var (companyId, accounts) = await CreateCompanyWithChartAsync(tenantId, "MAIN");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetPeriodCoveringAsync(tenantId, companyId, date);
        await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["1000"], "Hold for audit", Actor));

        await WithLocksAsync(tenantId, service => service.ReleaseAccountAsync(
            companyId, periodId, accounts["1000"], "Audit evidence accepted", "controller-tester"));

        var request = Post(companyId, date, "Bank deposit after release", accounts["1000"], accounts["3000"], 60m);
        var result = await WithPostingAsync(tenantId, service => service.PostAsync(request, Actor));
        result.Replayed.Should().BeFalse();

        var lockRows = await WithLocksAsync(tenantId, service => service.ListLocksAsync(companyId, periodId));
        var row = lockRows.Should().ContainSingle().Subject;
        row.IsLocked.Should().BeFalse();
        row.LockReason.Should().Be("Hold for audit");
        row.ReleasedBy.Should().Be("controller-tester");
        row.ReleaseReason.Should().Be("Audit evidence accepted");
        row.ReleasedAtUtc.Should().NotBeNull();
    }

    [PostgreSqlFact]
    public async Task LockedAccount_BlocksReversalOfAJournalInThePeriod()
    {
        var tenantId = $"lock-{Guid.NewGuid():N}";
        var (companyId, accounts) = await CreateCompanyWithChartAsync(tenantId, "MAIN");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetPeriodCoveringAsync(tenantId, companyId, date);
        var original = await WithPostingAsync(tenantId, service => service.PostAsync(
            Post(companyId, date, "Sale", accounts["1000"], accounts["3000"], 90m), Actor));

        await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["1000"], "Period under review", Actor));

        var reverse = async () => await WithPostingAsync(tenantId, service => service.ReverseAsync(
            new ReverseJournalRequest(companyId, original.JournalEntryId, "Customer returned goods", date), Actor));
        (await reverse.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*locked*");
    }

    [PostgreSqlFact]
    public async Task LockAdministration_RejectsGroupAccountsForeignAccountsAndConflicts()
    {
        var tenantId = $"lock-{Guid.NewGuid():N}";
        var (companyId, accounts) = await CreateCompanyWithChartAsync(tenantId, "ALPHA");
        var (_, foreignAccounts) = await CreateCompanyWithChartAsync(tenantId, "BETA");
        var periodId = await GetPeriodCoveringAsync(tenantId, companyId, DateOnly.FromDateTime(DateTime.UtcNow));

        var group = async () => await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["9000"], "Summary lock", Actor));
        (await group.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*group account*");

        var foreign = async () => await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, foreignAccounts["1000"], "Cross-company lock", Actor));
        (await foreign.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*not found in this company*");

        await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["1000"], "First lock", Actor));

        var duplicate = async () => await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["1000"], "Second lock", Actor));
        await duplicate.Should().ThrowAsync<FinancePostingConflictException>();

        var unlockedRelease = async () => await WithLocksAsync(tenantId, service => service.ReleaseAccountAsync(
            companyId, periodId, accounts["2000"], "Never locked", Actor));
        await unlockedRelease.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task LockRequests_RequireAReason()
    {
        var tenantId = $"lock-{Guid.NewGuid():N}";
        var (companyId, accounts) = await CreateCompanyWithChartAsync(tenantId, "MAIN");
        var periodId = await GetPeriodCoveringAsync(tenantId, companyId, DateOnly.FromDateTime(DateTime.UtcNow));

        var act = async () => await WithLocksAsync(tenantId, service => service.LockAccountAsync(
            companyId, periodId, accounts["1000"], "   ", Actor));

        await act.Should().ThrowAsync<FinancePostingValidationException>();
    }

    private static DateOnly OtherMonthDate(DateOnly date)
    {
        var month = date.Month == 1 ? 2 : 1;
        return new DateOnly(date.Year, month, 15);
    }

    private static JournalPostingRequest Post(
        int companyId,
        DateOnly postingDate,
        string description,
        int debitAccountId,
        int creditAccountId,
        decimal amount) => new(
        companyId,
        postingDate,
        "USD",
        description,
        "Manual",
        $"doc-{Guid.NewGuid():N}",
        "Post",
        1,
        [
            new JournalLineInput(debitAccountId, description, amount, 0m),
            new JournalLineInput(creditAccountId, description, 0m, amount)
        ]);

    private async Task<(int CompanyId, Dictionary<string, int> Accounts)> CreateCompanyWithChartAsync(
        string tenantId,
        string code)
    {
        int companyId;
        await using (var context = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = code,
                LegalName = $"{code} Legal Name",
                BaseCurrency = "USD",
                IsActive = true
            };
            context.Companies.Add(company);
            await context.SaveChangesAsAsync(Actor);
            companyId = company.Id;
        }

        var accounts = new Dictionary<string, int>();
        await using (var context = _fixture.CreateContext(tenantId))
        {
            var group = new ChartOfAccount
            {
                CompanyId = companyId,
                AccountCode = "9000",
                Name = "Summary",
                AccountType = "Summary",
                IsGroupAccount = true,
                IsActive = true
            };
            context.ChartOfAccounts.Add(group);
            await context.SaveChangesAsAsync(Actor);
            accounts["9000"] = group.Id;
        }

        foreach (var (accountCode, name, type) in new[]
                 {
                     ("1000", "Bank", "Asset"),
                     ("2000", "Payables", "Liability"),
                     ("3000", "Opening Equity", "Equity")
                 })
        {
            await using var context = _fixture.CreateContext(tenantId);
            var account = new ChartOfAccount
            {
                CompanyId = companyId,
                AccountCode = accountCode,
                Name = name,
                AccountType = type,
                IsGroupAccount = false,
                IsActive = true
            };
            context.ChartOfAccounts.Add(account);
            await context.SaveChangesAsAsync(Actor);
            accounts[accountCode] = account.Id;
        }

        await using (var context = _fixture.CreateContext(tenantId))
        {
            var unitOfWork = new UnitOfWork(context);
            var calendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
            var year = DateTime.UtcNow.Year;
            await calendar.CreateFiscalYearAsync(
                new CreateFiscalYearRequest(companyId, $"FY{year}", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
                Actor);
        }

        return (companyId, accounts);
    }

    private async Task<int> GetPeriodCoveringAsync(string tenantId, int companyId, DateOnly date)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var period = await context.FiscalPeriods.SingleAsync(
            candidate => candidate.CompanyId == companyId &&
                         candidate.StartDate <= date && candidate.EndDate >= date,
            CancellationToken.None);
        return period.Id;
    }

    private async Task<JournalPostingResult> WithPostingAsync(
        string tenantId,
        Func<JournalPostingService, Task<JournalPostingResult>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var service = new JournalPostingService(
            context,
            unitOfWork,
            new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance),
            new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
        return await action(service);
    }

    private async Task<T> WithLocksAsync<T>(string tenantId, Func<FiscalAccountLockService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var service = new FiscalAccountLockService(context, unitOfWork, NullLogger<FiscalAccountLockService>.Instance);
        return await action(service);
    }
}
