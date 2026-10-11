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
/// Relational acceptance for the year-end adjustment period (period 13): one per fiscal year on the
/// year-end day, reachable only by explicit targeting, open after the regular December period closes,
/// with reversals inheriting the target. Requires real PostgreSQL semantics and is skipped unless
/// RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class AdjustmentPeriodPostgreSqlTests
{
    private const string Actor = "adjustment-period-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public AdjustmentPeriodPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task AdjustmentPeriod_IsCreatedOncePerYearOnTheFiscalYearEnd()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, _) = await CreateCompanyWithYearAsync(tenantId);

        var period = await WithCalendarAsync(tenantId, calendar =>
            calendar.CreateAdjustmentPeriodAsync(companyId, yearId, Actor));

        period.PeriodNumber.Should().Be(13);
        period.IsAdjustment.Should().BeTrue();
        period.StartDate.Should().Be(YearEnd());
        period.EndDate.Should().Be(YearEnd());
        period.Name.Should().EndWith("P13 Adjustments");

        var second = async () => await WithCalendarAsync(tenantId, calendar =>
            calendar.CreateAdjustmentPeriodAsync(companyId, yearId, Actor));
        await second.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task AdjustmentPosting_LandsOnYearEndAfterDecemberCloses()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, accounts) = await CreateCompanyWithYearAsync(tenantId);
        var adjustmentId = await CreateAdjustmentAsync(tenantId, companyId, yearId);
        var decemberId = await GetPeriodIdAsync(tenantId, companyId, 12);
        await WithCalendarAsync(tenantId, calendar => calendar.ClosePeriodAsync(companyId, decemberId, Actor));

        var regular = Post(companyId, YearEnd(), "Regular year-end", accounts["1000"], accounts["3000"], 100m);
        var closed = async () => await WithPostingAsync(tenantId, service => service.PostAsync(regular, Actor));
        (await closed.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*closed to posting*");

        var adjustment = Post(companyId, YearEnd(), "Year-end correction", accounts["1000"], accounts["3000"], 100m);
        var result = await WithPostingAsync(tenantId, service =>
            service.PostAsync(adjustment with { FiscalPeriodId = adjustmentId }, Actor));

        result.Replayed.Should().BeFalse();
        var stored = await ReadJournalAsync(tenantId, result.JournalEntryId);
        stored.FiscalPeriodId.Should().Be(adjustmentId);
    }

    [PostgreSqlFact]
    public async Task RegularPosting_OnYearEndStaysOnDecemberWhileAdjustmentExists()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, accounts) = await CreateCompanyWithYearAsync(tenantId);
        await CreateAdjustmentAsync(tenantId, companyId, yearId);

        var regular = Post(companyId, YearEnd(), "December close", accounts["1000"], accounts["3000"], 40m);
        var result = await WithPostingAsync(tenantId, service => service.PostAsync(regular, Actor));

        var stored = await ReadJournalAsync(tenantId, result.JournalEntryId);
        stored.FiscalPeriodId.Should().BeNull("regular journals map to their period by date, not the adjustment");
    }

    [PostgreSqlFact]
    public async Task AdjustmentPosting_RejectsDatesOtherThanTheYearEndAndRegularTargets()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, accounts) = await CreateCompanyWithYearAsync(tenantId);
        var adjustmentId = await CreateAdjustmentAsync(tenantId, companyId, yearId);
        var januaryId = await GetPeriodIdAsync(tenantId, companyId, 1);

        var wrongDay = Post(companyId, YearEnd().AddDays(-1), "Wrong day", accounts["1000"], accounts["3000"], 10m)
            with { FiscalPeriodId = adjustmentId };
        var rejectedDay = async () => await WithPostingAsync(tenantId, service => service.PostAsync(wrongDay, Actor));
        (await rejectedDay.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*accepts postings only on the fiscal year end*");

        var regularTarget = Post(companyId, new DateOnly(YearEnd().Year, 1, 15), "Regular target", accounts["1000"], accounts["3000"], 10m)
            with { FiscalPeriodId = januaryId };
        var rejectedTarget = async () => await WithPostingAsync(tenantId, service => service.PostAsync(regularTarget, Actor));
        (await rejectedTarget.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*is not an adjustment period*");
    }

    [PostgreSqlFact]
    public async Task ClosedAdjustmentPeriod_RejectsFurtherAdjustments()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, accounts) = await CreateCompanyWithYearAsync(tenantId);
        var adjustmentId = await CreateAdjustmentAsync(tenantId, companyId, yearId);
        await WithCalendarAsync(tenantId, calendar => calendar.ClosePeriodAsync(companyId, adjustmentId, Actor));

        var adjustment = Post(companyId, YearEnd(), "Late adjustment", accounts["1000"], accounts["3000"], 10m)
            with { FiscalPeriodId = adjustmentId };
        var act = async () => await WithPostingAsync(tenantId, service => service.PostAsync(adjustment, Actor));

        (await act.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*closed to posting*");
    }

    [PostgreSqlFact]
    public async Task Reversal_OfAnAdjustmentJournal_InheritsTheAdjustmentTarget()
    {
        var tenantId = $"adjust-{Guid.NewGuid():N}";
        var (companyId, yearId, accounts) = await CreateCompanyWithYearAsync(tenantId);
        var adjustmentId = await CreateAdjustmentAsync(tenantId, companyId, yearId);
        var original = Post(companyId, YearEnd(), "Adjustment", accounts["1000"], accounts["3000"], 25m)
            with { FiscalPeriodId = adjustmentId };
        var posted = await WithPostingAsync(tenantId, service => service.PostAsync(original, Actor));

        var reversal = await WithPostingAsync(tenantId, service => service.ReverseAsync(
            new ReverseJournalRequest(companyId, posted.JournalEntryId, "Adjustment posted in error", YearEnd()),
            Actor));

        var stored = await ReadJournalAsync(tenantId, reversal.JournalEntryId);
        stored.ReversalOfJournalEntryId.Should().Be(posted.JournalEntryId);
        stored.FiscalPeriodId.Should().Be(adjustmentId);
    }

    private static DateOnly YearEnd() => new(DateTime.UtcNow.Year, 12, 31);

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

    private async Task<int> CreateAdjustmentAsync(string tenantId, int companyId, int yearId)
    {
        var period = await WithCalendarAsync(tenantId, calendar =>
            calendar.CreateAdjustmentPeriodAsync(companyId, yearId, Actor));
        return period.Id;
    }

    private async Task<(int CompanyId, int YearId, Dictionary<string, int> Accounts)> CreateCompanyWithYearAsync(
        string tenantId)
    {
        int companyId;
        await using (var context = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "MAIN",
                LegalName = "MAIN Legal Name",
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
            foreach (var (code, name, type) in new[] { ("1000", "Bank", "Asset"), ("3000", "Equity", "Equity") })
            {
                var account = new ChartOfAccount
                {
                    CompanyId = companyId,
                    AccountCode = code,
                    Name = name,
                    AccountType = type,
                    IsGroupAccount = false,
                    IsActive = true
                };
                context.ChartOfAccounts.Add(account);
                await context.SaveChangesAsAsync(Actor);
                accounts[code] = account.Id;
            }
        }

        var year = await WithCalendarAsync(tenantId, calendar => calendar.CreateFiscalYearAsync(
            new CreateFiscalYearRequest(companyId, $"FY{YearEnd().Year}", new DateOnly(YearEnd().Year, 1, 1), YearEnd()),
            Actor));
        return (companyId, year.Id, accounts);
    }

    private async Task<int> GetPeriodIdAsync(string tenantId, int companyId, int periodNumber)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var period = await context.FiscalPeriods.SingleAsync(
            candidate => candidate.CompanyId == companyId && candidate.PeriodNumber == periodNumber,
            CancellationToken.None);
        return period.Id;
    }

    private async Task<JournalEntry> ReadJournalAsync(string tenantId, int journalEntryId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await context.JournalEntries.SingleAsync(entry => entry.Id == journalEntryId, CancellationToken.None);
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

    private async Task<T> WithCalendarAsync<T>(string tenantId, Func<FiscalCalendarService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        return await action(new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance));
    }
}
