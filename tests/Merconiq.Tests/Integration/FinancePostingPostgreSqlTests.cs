using System.Data.Common;
using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for the G1 finance kernel: balanced immutable postings, fiscal-period
/// gates, source-action idempotency, source-linked reversals, and ledger-backed trial balance.
/// These tests require real PostgreSQL semantics and are skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class FinancePostingPostgreSqlTests
{
    private const string Actor = "finance-integration-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public FinancePostingPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task PostAsync_BalancedRequest_PersistsImmutableBalancedJournalWithSnapshots()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 1000m);

        var result = await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));

        result.Replayed.Should().BeFalse();
        result.TotalDebit.Should().Be(1000m);
        result.TotalCredit.Should().Be(1000m);
        result.JournalNumber.Should().StartWith("JV");

        await using var verify = _fixture.CreateContext(tenantId);
        var journal = await verify.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.Id == result.JournalEntryId, CancellationToken.None);
        journal.CompanyId.Should().Be(companyId);
        journal.CurrencyCode.Should().Be("USD");
        journal.SourceDocumentType.Should().Be("OpeningBalance");
        journal.SourceDocumentId.Should().Be("doc-1");
        journal.SourceAction.Should().Be("Post");
        journal.SourceVersion.Should().Be(1);
        journal.PostedBy.Should().Be(Actor);
        journal.TotalDebit.Should().Be(journal.TotalCredit).And.Be(1000m);
        journal.Lines.Should().HaveCount(2);
        var debitLine = journal.Lines.Single(line => line.Debit > 0);
        debitLine.AccountCodeSnapshot.Should().Be("1000");
        debitLine.Credit.Should().Be(0m);
        var creditLine = journal.Lines.Single(line => line.Credit > 0);
        creditLine.AccountCodeSnapshot.Should().Be("3000");
        creditLine.Debit.Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task PostAsync_UnbalancedRequest_IsRejectedAndPersistsNothing()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 100m) with
        {
            Lines =
            [
                new JournalLineInput(bankAccountId, "Bank", 100m, 0m),
                new JournalLineInput(equityAccountId, "Equity", 0m, 99.99m)
            ]
        };

        var act = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));

        await act.Should().ThrowAsync<FinancePostingValidationException>();
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.JournalEntries.CountAsync()).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task PostAsync_NoFiscalPeriodForDate_IsRejected()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 100m)
            with { PostingDate = new DateOnly(2030, 6, 15) };

        var act = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));

        (await act.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*No fiscal period covers*");
    }

    [PostgreSqlFact]
    public async Task PostAsync_ClosedPeriod_IsRejectedUntilAuthorizedReopen()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var postingDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = await GetPeriodCoveringAsync(tenantId, companyId, postingDate);

        await WithFiscalCalendarAsync(tenantId, calendar => calendar.ClosePeriodAsync(companyId, periodId, Actor));

        var request = Balanced(companyId, bankAccountId, equityAccountId, 250m);
        var rejected = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));
        (await rejected.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*closed to posting*");

        await WithFiscalCalendarAsync(
            tenantId, calendar => calendar.ReopenPeriodAsync(companyId, periodId, "Statutory correction review", Actor));

        var result = await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));
        result.Replayed.Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task PostAsync_GroupOrForeignAccount_IsRejected()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId);
        var (bankAccountId, _) = await CreateAccountsAsync(tenantId, companyId);
        await CreateFiscalYearAsync(tenantId, companyId);
        var groupId = await CreateGroupAccountAsync(tenantId, companyId);

        var groupRequest = new JournalPostingRequest(
            companyId, Today(), "USD", "Group posting", "Manual", "doc-group", "Post", 1,
            [
                new JournalLineInput(groupId, null, 50m, 0m),
                new JournalLineInput(bankAccountId, null, 0m, 50m)
            ]);
        var groupAct = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(groupRequest, Actor));
        (await groupAct.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*group account*");

        // An account owned by a different company must never receive postings.
        var otherCompanyId = await CreateCompanyAsync(tenantId, "OTHER");
        var (foreignAccountId, _) = await CreateAccountsAsync(tenantId, otherCompanyId);
        var foreignRequest = new JournalPostingRequest(
            companyId, Today(), "USD", "Foreign account", "Manual", "doc-foreign", "Post", 1,
            [
                new JournalLineInput(foreignAccountId, null, 50m, 0m),
                new JournalLineInput(bankAccountId, null, 0m, 50m)
            ]);
        var foreignAct = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(foreignRequest, Actor));
        (await foreignAct.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*does not exist in this company*");
    }

    [PostgreSqlFact]
    public async Task PostAsync_CommittedSourceAction_SamePayloadReplaysWithoutSecondJournal()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 400m);

        var first = await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));
        var second = await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.JournalEntryId.Should().Be(first.JournalEntryId);
        second.JournalNumber.Should().Be(first.JournalNumber);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.JournalEntries.CountAsync()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task PostAsync_CommittedSourceAction_ConflictingPayload_IsRejected()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var original = Balanced(companyId, bankAccountId, equityAccountId, 400m);
        await WithPostingServiceAsync(tenantId, service => service.PostAsync(original, Actor));

        var conflicting = Balanced(companyId, bankAccountId, equityAccountId, 500m);
        var act = async () => await WithPostingServiceAsync(tenantId, service => service.PostAsync(conflicting, Actor));

        (await act.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*different payload*");
    }

    [PostgreSqlFact]
    public async Task PostAsync_ConcurrentSameSourceAction_CreatesExactlyOneJournal()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 600m);

        async Task<JournalPostingResult> PostAsWorkerAsync(string worker) =>
            await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, worker));

        var first = PostAsWorkerAsync("worker-one");
        var second = PostAsWorkerAsync("worker-two");
        await Task.WhenAll(first, second);

        var results = new[] { await first, await second };
        results.Should().Contain(result => !result.Replayed);
        results.Select(result => result.JournalEntryId).Distinct().Should().HaveCount(1);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.JournalEntries.CountAsync()).Should().Be(1);
        (await verify.JournalLines.CountAsync()).Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task ReverseAsync_PostsSwappedLinesOnce_ReferencesOriginal_AndBlocksSecondReversal()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var original = await WithPostingServiceAsync(
            tenantId, service => service.PostAsync(Balanced(companyId, bankAccountId, equityAccountId, 300m), Actor));

        var reversalRequest = new ReverseJournalRequest(companyId, original.JournalEntryId, "Duplicate posting");
        var reversal = await WithPostingServiceAsync(tenantId, service => service.ReverseAsync(reversalRequest, Actor));
        var replay = await WithPostingServiceAsync(tenantId, service => service.ReverseAsync(reversalRequest, Actor));

        reversal.Replayed.Should().BeFalse();
        reversal.TotalDebit.Should().Be(300m);
        replay.Replayed.Should().BeTrue();
        replay.JournalEntryId.Should().Be(reversal.JournalEntryId);

        await using var verify = _fixture.CreateContext(tenantId);
        var reversalJournal = await verify.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.Id == reversal.JournalEntryId, CancellationToken.None);
        reversalJournal.ReversalOfJournalEntryId.Should().Be(original.JournalEntryId);
        reversalJournal.ReversalReason.Should().Be("Duplicate posting");
        reversalJournal.SourceAction.Should().Be("Reverse");
        reversalJournal.Lines.Should().HaveCount(2);
        reversalJournal.Lines.Single(line => line.Credit > 0).ChartOfAccountId.Should().Be(bankAccountId);
        reversalJournal.Lines.Single(line => line.Debit > 0).ChartOfAccountId.Should().Be(equityAccountId);

        var secondReversal = new ReverseJournalRequest(companyId, reversal.JournalEntryId, "Re-reverse");
        var blocked = async () => await WithPostingServiceAsync(tenantId, service => service.ReverseAsync(secondReversal, Actor));
        (await blocked.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*cannot be reversed*");
    }

    [PostgreSqlFact]
    public async Task GetTrialBalance_MatchesIndependentlyCalculatedFixtureAndAlwaysBalances()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var inventoryAccountId = await CreateSingleAccountAsync(tenantId, companyId, "1300", "Inventory");
        var payablesAccountId = await CreateSingleAccountAsync(tenantId, companyId, "2000", "Accounts Payable");

        await WithPostingServiceAsync(tenantId, service => service.PostAsync(
            Balanced(companyId, bankAccountId, equityAccountId, 1000m), Actor));
        await WithPostingServiceAsync(tenantId, service => service.PostAsync(new JournalPostingRequest(
            companyId, Today(), "USD", "Buy stock on account", "Manual", "doc-2", "Post", 1,
            [
                new JournalLineInput(inventoryAccountId, "Inventory", 600m, 0m),
                new JournalLineInput(payablesAccountId, "AP", 0m, 600m)
            ]), Actor));
        await WithPostingServiceAsync(tenantId, service => service.PostAsync(new JournalPostingRequest(
            companyId, Today(), "USD", "Pay supplier", "Manual", "doc-3", "Post", 1,
            [
                new JournalLineInput(payablesAccountId, "AP", 400m, 0m),
                new JournalLineInput(bankAccountId, "Bank", 0m, 400m)
            ]), Actor));

        var trialBalance = await WithPostingServiceAsync(
            tenantId, service => service.GetTrialBalanceAsync(companyId, null, null));

        // Independent fixture values: opening equity 1,000 to Bank; buy stock on account 600;
        // pay the supplier 400. Debits and credits each total 2,000.
        trialBalance.TotalDebit.Should().Be(2000m);
        trialBalance.TotalCredit.Should().Be(2000m);
        var bank = trialBalance.Rows.Single(row => row.AccountCode == "1000");
        bank.Debit.Should().Be(1000m);
        bank.Credit.Should().Be(400m);
        trialBalance.Rows.Single(row => row.AccountCode == "3000").Credit.Should().Be(1000m);
        trialBalance.Rows.Single(row => row.AccountCode == "1300").Debit.Should().Be(600m);
        var payables = trialBalance.Rows.Single(row => row.AccountCode == "2000");
        payables.Credit.Should().Be(600m);
        payables.Debit.Should().Be(400m);

        // An as-of window that excludes the second posting reports only the opening journal.
        var asOfYesterday = await WithPostingServiceAsync(
            tenantId, service => service.GetTrialBalanceAsync(companyId, null, Today().AddDays(-1)));
        asOfYesterday.TotalDebit.Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task JournalRows_AreAppendOnly_UpdateAttemptsThrow()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var posted = await WithPostingServiceAsync(
            tenantId, service => service.PostAsync(Balanced(companyId, bankAccountId, equityAccountId, 50m), Actor));

        await using var context = _fixture.CreateContext(tenantId);
        var journal = await context.JournalEntries.SingleAsync(
            entry => entry.Id == posted.JournalEntryId, CancellationToken.None);
        journal.Description = "Tampered";
        var act = () => context.SaveChangesAsAsync(Actor);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    [PostgreSqlFact]
    public async Task PostAsync_FailureAfterJournalInsert_RollsBackAllEffects()
    {
        var tenantId = $"finance-{Guid.NewGuid():N}";
        var (companyId, bankAccountId, equityAccountId) = await CreatePostingReadyCompanyAsync(tenantId);
        var request = Balanced(companyId, bankAccountId, equityAccountId, 700m);

        // The audit phase runs after the journal inserts inside the posting transaction;
        // failing there must roll the journal back with it.
        await using (var failingContext = _fixture.CreateContext(tenantId, "finance-faulted", new AuditFailureInterceptor()))
        {
            var service = BuildPostingService(failingContext);
            var act = () => service.PostAsync(request, Actor);
            (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException!.Message
                .Should().Contain("Injected audit failure");
        }

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.JournalEntries.CountAsync()).Should().Be(0);

        // The tenant remains healthy for a clean retry.
        var retry = await WithPostingServiceAsync(tenantId, service => service.PostAsync(request, Actor));
        retry.Replayed.Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task Journals_AreTenantIsolated()
    {
        var tenantOne = $"finance-{Guid.NewGuid():N}";
        var tenantTwo = $"finance-{Guid.NewGuid():N}";
        var companyIdOne = await CreateCompanyAsync(tenantOne);
        var (bankAccountIdOne, equityAccountIdOne) = await CreateAccountsAsync(tenantOne, companyIdOne);
        await CreateFiscalYearAsync(tenantOne, companyIdOne);
        var posted = await WithPostingServiceAsync(
            tenantOne, service => service.PostAsync(Balanced(companyIdOne, bankAccountIdOne, equityAccountIdOne, 80m), Actor));

        var companyIdTwo = await CreateCompanyAsync(tenantTwo);
        await using var tenantTwoContext = _fixture.CreateContext(tenantTwo);
        var serviceTwo = BuildPostingService(tenantTwoContext);
        (await serviceTwo.GetJournalAsync(companyIdTwo, posted.JournalEntryId)).Should().BeNull();
        (await tenantTwoContext.JournalEntries.CountAsync()).Should().Be(0);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static JournalPostingRequest Balanced(
        int companyId,
        int bankAccountId,
        int equityAccountId,
        decimal amount) => new(
        companyId,
        Today(),
        "USD",
        "Opening balance",
        "OpeningBalance",
        "doc-1",
        "Post",
        1,
        [
            new JournalLineInput(bankAccountId, "Bank", amount, 0m),
            new JournalLineInput(equityAccountId, "Equity", 0m, amount)
        ]);

    private static JournalPostingService BuildPostingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new JournalPostingService(
            context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance);
    }

    private static FiscalCalendarService BuildFiscalCalendar(InventoryDbContext context) =>
        new(context, new UnitOfWork(context), NullLogger<FiscalCalendarService>.Instance);

    private async Task<T> WithPostingServiceAsync<T>(string tenantId, Func<JournalPostingService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildPostingService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<T> WithFiscalCalendarAsync<T>(string tenantId, Func<FiscalCalendarService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildFiscalCalendar(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<(int CompanyId, int BankAccountId, int EquityAccountId)> CreatePostingReadyCompanyAsync(
        string tenantId,
        string code = "MAIN")
    {
        var companyId = await CreateCompanyAsync(tenantId, code);
        var (bankAccountId, equityAccountId) = await CreateAccountsAsync(tenantId, companyId);
        await CreateFiscalYearAsync(tenantId, companyId);
        return (companyId, bankAccountId, equityAccountId);
    }

    private async Task<int> CreateCompanyAsync(string tenantId, string code = "MAIN")
    {
        await using var context = _fixture.CreateContext(tenantId);
        var company = new Company
        {
            Code = code,
            LegalName = $"{code} Legal Name",
            BaseCurrency = "USD",
            IsActive = true
        };
        context.Companies.Add(company);
        await context.SaveChangesAsAsync(Actor);
        return company.Id;
    }

    private async Task<(int BankAccountId, int EquityAccountId)> CreateAccountsAsync(string tenantId, int companyId)
    {
        var bankAccountId = await CreateSingleAccountAsync(tenantId, companyId, "1000", "Bank");
        var equityAccountId = await CreateSingleAccountAsync(tenantId, companyId, "3000", "Opening Equity");
        return (bankAccountId, equityAccountId);
    }

    private async Task<int> CreateSingleAccountAsync(string tenantId, int companyId, string code, string name)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountCode = code,
            Name = name,
            AccountType = "Asset",
            IsGroupAccount = false,
            IsActive = true
        };
        context.ChartOfAccounts.Add(account);
        await context.SaveChangesAsAsync(Actor);
        return account.Id;
    }

    private async Task<int> CreateGroupAccountAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountCode = "9000",
            Name = "Summary Group",
            AccountType = "Summary",
            IsGroupAccount = true,
            IsActive = true
        };
        context.ChartOfAccounts.Add(account);
        await context.SaveChangesAsAsync(Actor);
        return account.Id;
    }

    private async Task CreateFiscalYearAsync(string tenantId, int companyId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var yearEnd = new DateOnly(today.Year, 12, 31);
        await WithFiscalCalendarAsync(tenantId, calendar => calendar.CreateFiscalYearAsync(
            new CreateFiscalYearRequest(companyId, $"FY{today.Year}", yearStart, yearEnd), Actor));
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

    /// <summary>Fails the audit phase of the two-phase save after business rows have inserted.</summary>
    private sealed class AuditFailureInterceptor : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("AuditLogs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected audit failure inside the posting transaction.");
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
