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
/// Relational acceptance for the fixed-asset lifecycle: capitalization against a payment account, monthly
/// straight-line depreciation that replays instead of double-charging, disposal that requires depreciation
/// through the disposal date and posts the exact gain or loss, and company isolation. Requires real
/// PostgreSQL semantics and is skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class FixedAssetPostgreSqlTests
{
    private const string Actor = "fixed-asset-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public FixedAssetPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Register_RejectsDuplicateCodesAndInvalidSchedules()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        await WithServiceAsync(tenantId, service => service.RegisterAsync(Van(setup.CompanyId), Actor));

        var duplicate = async () => await WithServiceAsync(tenantId, service => service.RegisterAsync(Van(setup.CompanyId), Actor));
        await duplicate.Should().ThrowAsync<FinancePostingConflictException>();

        var salvage = async () => await WithServiceAsync(tenantId, service => service.RegisterAsync(
            Van(setup.CompanyId) with { AssetCode = "FA-2", SalvageValue = 1200m }, Actor));
        await salvage.Should().ThrowAsync<FinancePostingValidationException>();

        var earlyStart = async () => await WithServiceAsync(tenantId, service => service.RegisterAsync(
            Van(setup.CompanyId) with { AssetCode = "FA-3", DepreciationStartDate = new DateOnly(Year() - 1, 12, 1) }, Actor));
        await earlyStart.Should().ThrowAsync<FinancePostingValidationException>();
    }

    [PostgreSqlFact]
    public async Task Capitalize_PostsCostAgainstThePaymentAccountExactlyOnce()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await WithServiceAsync(tenantId, service => service.RegisterAsync(Van(setup.CompanyId), Actor));

        var capitalized = await WithServiceAsync(tenantId, service =>
            service.CapitalizeAsync(setup.CompanyId, asset.Id, setup.Accounts["1000"], Actor));

        capitalized.Status.Should().Be(FixedAssetStatus.Capitalized);
        var lines = await JournalLinesAsync(tenantId, capitalized.CapitalizationJournalEntryId!.Value);
        lines.Should().HaveCount(2);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["1500"]).Debit.Should().Be(1200m);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["1000"]).Credit.Should().Be(1200m);

        var again = async () => await WithServiceAsync(tenantId, service =>
            service.CapitalizeAsync(setup.CompanyId, asset.Id, setup.Accounts["1000"], Actor));
        await again.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task DepreciationRun_PostsEachDueMonth_AndRerunsReplayWithoutDoubleCharging()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await CapitalizedVanAsync(tenantId, setup);

        var first = await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));
        first.Postings.Should().HaveCount(3);
        first.Postings.Select(posting => posting.Amount).Should().AllBeEquivalentTo(100m);
        first.Postings.Should().OnlyContain(posting => !posting.Replayed);

        var rerun = await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));
        rerun.Postings.Should().HaveCount(3);
        rerun.Postings.Should().OnlyContain(posting => posting.Replayed);
        (await CountDepreciationJournalsAsync(tenantId, asset.Id)).Should().Be(3);

        var position = await WithServiceAsync(tenantId, service => service.GetAsync(setup.CompanyId, asset.Id));
        position!.PostedDepreciationPeriods.Should().Be(3);
        position.AccumulatedDepreciation.Should().Be(300m);
        position.NetBookValue.Should().Be(900m);

        var yearEnd = await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(12), Actor));
        yearEnd.Postings.Where(posting => !posting.Replayed).Should().HaveCount(9);
        var closed = await WithServiceAsync(tenantId, service => service.GetAsync(setup.CompanyId, asset.Id));
        closed!.NetBookValue.Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task Disposal_RequiresDepreciationThroughTheDisposalDate()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await CapitalizedVanAsync(tenantId, setup);

        var early = async () => await WithServiceAsync(tenantId, service => service.DisposeAsync(
            setup.CompanyId, asset.Id, MonthEnd(3), 1000m, setup.Accounts["1000"], "Sold to a dealer", Actor));

        (await early.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*posted for 0 of 3 months*");
    }

    [PostgreSqlFact]
    public async Task Disposal_WithProceedsAboveNetBookValue_PostsTheGainAndStaysBalanced()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await CapitalizedVanAsync(tenantId, setup);
        await WithServiceAsync(tenantId, service => service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));

        var disposed = await WithServiceAsync(tenantId, service => service.DisposeAsync(
            setup.CompanyId, asset.Id, MonthEnd(3), 1000m, setup.Accounts["1000"], "Sold to a dealer", Actor));

        disposed.Status.Should().Be(FixedAssetStatus.Disposed);
        disposed.DisposalProceeds.Should().Be(1000m);
        var lines = await JournalLinesAsync(tenantId, disposed.DisposalJournalEntryId!.Value);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["1000"]).Debit.Should().Be(1000m);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["1590"]).Debit.Should().Be(300m);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["1500"]).Credit.Should().Be(1200m);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["4900"]).Credit.Should().Be(100m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));

        var trialBalance = await WithPostingAsync(tenantId, service => service.GetTrialBalanceAsync(setup.CompanyId, null, null));
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);
    }

    [PostgreSqlFact]
    public async Task Disposal_WithProceedsBelowNetBookValue_PostsTheLoss()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await CapitalizedVanAsync(tenantId, setup);
        await WithServiceAsync(tenantId, service => service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));

        var disposed = await WithServiceAsync(tenantId, service => service.DisposeAsync(
            setup.CompanyId, asset.Id, MonthEnd(3), 500m, setup.Accounts["1000"], "Scrapped after damage", Actor));

        var lines = await JournalLinesAsync(tenantId, disposed.DisposalJournalEntryId!.Value);
        lines.Single(line => line.ChartOfAccountId == setup.Accounts["5900"]).Debit.Should().Be(400m);
        lines.Should().NotContain(line => line.ChartOfAccountId == setup.Accounts["4900"]);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));
    }

    [PostgreSqlFact]
    public async Task DisposedAsset_IsNeverDepreciatedAgain()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await CapitalizedVanAsync(tenantId, setup);
        await WithServiceAsync(tenantId, service => service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));
        await WithServiceAsync(tenantId, service => service.DisposeAsync(
            setup.CompanyId, asset.Id, MonthEnd(3), 1000m, setup.Accounts["1000"], "Sold", Actor));

        var later = await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(12), Actor));

        later.Postings.Should().NotContain(posting => posting.FixedAssetId == asset.Id);
        (await CountDepreciationJournalsAsync(tenantId, asset.Id)).Should().Be(3);
    }

    [PostgreSqlFact]
    public async Task DepreciationJournal_CannotBeReversedGenerically()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        await CapitalizedVanAsync(tenantId, setup);
        var run = await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(1), Actor));
        var journalId = run.Postings.Single().JournalEntryId;

        var reverse = async () => await WithPostingAsync(tenantId, service => service.ReverseAsync(
            new ReverseJournalRequest(setup.CompanyId, journalId, "Wrong month", MonthEnd(1)), Actor));

        (await reverse.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*fixed-asset workflow*");
    }

    [PostgreSqlFact]
    public async Task DepreciationRun_WithAnUnmappedRole_FailsAndPostsNothing()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: false);
        await CapitalizedVanAsync(tenantId, setup);

        var run = async () => await WithServiceAsync(tenantId, service =>
            service.RunDepreciationAsync(setup.CompanyId, MonthEnd(3), Actor));

        (await run.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*DepreciationExpense*");
        (await CountAllDepreciationJournalsAsync(tenantId)).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task Assets_AreInvisibleToOtherCompanies()
    {
        var tenantId = $"fa-{Guid.NewGuid():N}";
        var setup = await CreateCompanyAsync(tenantId, "MAIN", mapAllRoles: true);
        var asset = await WithServiceAsync(tenantId, service => service.RegisterAsync(Van(setup.CompanyId), Actor));
        var otherCompanyId = await CreateBareCompanyAsync(tenantId, "OTHER");

        var position = await WithServiceAsync(tenantId, service => service.GetAsync(otherCompanyId, asset.Id));
        position.Should().BeNull();

        var capitalize = async () => await WithServiceAsync(tenantId, service =>
            service.CapitalizeAsync(otherCompanyId, asset.Id, setup.Accounts["1000"], Actor));
        (await capitalize.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*not found in this company*");
    }

    private static int Year() => DateTime.UtcNow.Year;

    private static DateOnly MonthEnd(int month) =>
        new(Year(), month, DateTime.DaysInMonth(Year(), month));

    private static RegisterFixedAssetRequest Van(int companyId) => new(
        companyId,
        "FA-001",
        "Delivery van",
        new DateOnly(Year(), 1, 1),
        new DateOnly(Year(), 1, 1),
        1200m,
        0m,
        12);

    private async Task<FixedAsset> CapitalizedVanAsync(string tenantId, CompanySetup setup)
    {
        var asset = await WithServiceAsync(tenantId, service => service.RegisterAsync(Van(setup.CompanyId), Actor));
        return await WithServiceAsync(tenantId, service =>
            service.CapitalizeAsync(setup.CompanyId, asset.Id, setup.Accounts["1000"], Actor));
    }

    private sealed record CompanySetup(int CompanyId, Dictionary<string, int> Accounts);

    private async Task<CompanySetup> CreateCompanyAsync(string tenantId, string code, bool mapAllRoles)
    {
        var companyId = await CreateBareCompanyAsync(tenantId, code);
        var accounts = new Dictionary<string, int>();
        await using (var context = _fixture.CreateContext(tenantId))
        {
            var definitions = new[]
            {
                ("1000", "Bank", "Asset", AccountClass.Asset),
                ("1500", "Fixed Assets", "Asset", AccountClass.Asset),
                ("1590", "Accumulated Depreciation", "Asset", AccountClass.Asset),
                ("4900", "Gain on Disposal", "Income", AccountClass.Income),
                ("5300", "Depreciation Expense", "Expense", AccountClass.Expense),
                ("5900", "Loss on Disposal", "Expense", AccountClass.Expense),
                ("9000", "Summary", "Summary", (AccountClass?)null)
            };
            foreach (var (accountCode, name, type, accountClass) in definitions)
            {
                var account = new ChartOfAccount
                {
                    CompanyId = companyId,
                    AccountCode = accountCode,
                    Name = name,
                    AccountType = type,
                    IsGroupAccount = accountClass is null,
                    AccountClass = accountClass,
                    IsActive = true
                };
                context.ChartOfAccounts.Add(account);
                await context.SaveChangesAsAsync(Actor);
                accounts[accountCode] = account.Id;
            }

            // Capitalization always needs the cost role; depreciation and disposal roles are optional so a
            // test can prove that a missing role fails the whole operation.
            var roles = new[]
            {
                (AccountPostingRole.FixedAssets, "1500"),
                (AccountPostingRole.AccumulatedDepreciation, "1590"),
                (AccountPostingRole.DepreciationExpense, "5300"),
                (AccountPostingRole.GainOnDisposal, "4900"),
                (AccountPostingRole.LossOnDisposal, "5900")
            };
            foreach (var (role, accountCode) in mapAllRoles ? roles : roles.Take(1))
            {
                context.CompanyAccountMappings.Add(new CompanyAccountMapping
                {
                    CompanyId = companyId,
                    Role = role,
                    ChartOfAccountId = accounts[accountCode],
                    IsActive = true
                });
            }
            await context.SaveChangesAsAsync(Actor);
        }

        await using (var context = _fixture.CreateContext(tenantId))
        {
            var unitOfWork = new UnitOfWork(context);
            await new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance)
                .CreateFiscalYearAsync(
                    new CreateFiscalYearRequest(companyId, $"FY{Year()}", new DateOnly(Year(), 1, 1), new DateOnly(Year(), 12, 31)),
                    Actor);
        }

        return new CompanySetup(companyId, accounts);
    }

    private async Task<int> CreateBareCompanyAsync(string tenantId, string code)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var company = new Company { Code = code, LegalName = $"{code} Legal Name", BaseCurrency = "USD", IsActive = true };
        context.Companies.Add(company);
        await context.SaveChangesAsAsync(Actor);
        return company.Id;
    }

    private async Task<List<JournalLine>> JournalLinesAsync(string tenantId, int journalEntryId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await context.JournalLines
            .Where(line => line.JournalEntryId == journalEntryId)
            .ToListAsync(CancellationToken.None);
    }

    private async Task<int> CountDepreciationJournalsAsync(string tenantId, int fixedAssetId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var key = fixedAssetId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await context.JournalEntries.CountAsync(entry =>
            entry.SourceDocumentType == "FixedAsset" &&
            entry.SourceDocumentId == key &&
            entry.SourceAction == "Depreciate",
            CancellationToken.None);
    }

    private async Task<int> CountAllDepreciationJournalsAsync(string tenantId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await context.JournalEntries.CountAsync(entry => entry.SourceAction == "Depreciate", CancellationToken.None);
    }

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<FixedAssetService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var journal = BuildJournal(context, unitOfWork);
        var service = new FixedAssetService(
            context,
            unitOfWork,
            journal,
            new AccountPostingPolicyService(context),
            NullLogger<FixedAssetService>.Instance);
        return await action(service);
    }

    private async Task<T> WithPostingAsync<T>(string tenantId, Func<JournalPostingService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        return await action(BuildJournal(context, unitOfWork));
    }

    private static JournalPostingService BuildJournal(InventoryDbContext context, UnitOfWork unitOfWork) =>
        new(
            context,
            unitOfWork,
            new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance),
            new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
}
