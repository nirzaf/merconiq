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
/// Relational acceptance for bank reconciliation: statements import only when they foot, matches are
/// one-to-one and checked for account, date and amount, withdrawn matches keep their history, and every
/// report figure follows from persisted statement lines and posted ledger lines. Reconciling never changes
/// ledger totals. Requires real PostgreSQL semantics and is skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class BankReconciliationPostgreSqlTests
{
    private const string Actor = "bank-reconciliation-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public BankReconciliationPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Statement_ThatFootsReconcilesWhenEveryLineIsMatched()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var journals = await PostBankLinesAsync(tenantId, ledger);
        var statementId = await ImportAsync(tenantId, ledger, closing: 720m, lines:
        [
            Line(1, "Opening deposit", 500m),
            Line(10, "Customer receipt", 300m),
            Line(20, "Rent", -80m)
        ]);
        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);
        await MatchAsync(tenantId, ledger, statementId, 2, journals.Sale);
        await MatchAsync(tenantId, ledger, statementId, 3, journals.Rent);

        var report = await ReportAsync(tenantId, ledger, statementId);

        report.LedgerBalance.Should().Be(720m);
        report.Difference.Should().Be(0m);
        report.IsReconciled.Should().BeTrue();
        report.MatchedStatementLineCount.Should().Be(3);
        report.UnmatchedLedgerLineCount.Should().Be(0);
        report.UnmatchedLedgerItems.Should().BeEmpty();
    }

    [PostgreSqlFact]
    public async Task DepositInTransit_IsATimingItemAndTheStatementStillReconciles()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var journals = await PostBankLinesAsync(tenantId, ledger);
        var statementId = await ImportAsync(tenantId, ledger, closing: 420m, lines:
        [
            Line(1, "Opening deposit", 500m),
            Line(20, "Rent", -80m)
        ]);
        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);
        await MatchAsync(tenantId, ledger, statementId, 2, journals.Rent);

        var report = await ReportAsync(tenantId, ledger, statementId);

        report.UnmatchedLedgerAmount.Should().Be(300m, "the sale is in the ledger but not yet on the statement");
        report.UnmatchedLedgerLineCount.Should().Be(1);
        report.AdjustedBankBalance.Should().Be(720m);
        report.AdjustedBookBalance.Should().Be(720m);
        report.Difference.Should().Be(0m);
        report.IsReconciled.Should().BeTrue();
        report.UnmatchedLedgerItems.Should().ContainSingle().Which.Amount.Should().Be(300m);
    }

    [PostgreSqlFact]
    public async Task BankFee_OnTheStatementOnly_BlocksFullReconciliation()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var journals = await PostBankLinesAsync(tenantId, ledger);
        var statementId = await ImportAsync(tenantId, ledger, closing: 705m, lines:
        [
            Line(1, "Opening deposit", 500m),
            Line(10, "Customer receipt", 300m),
            Line(20, "Rent", -80m),
            Line(31, "Bank fee", -15m)
        ]);
        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);
        await MatchAsync(tenantId, ledger, statementId, 2, journals.Sale);
        await MatchAsync(tenantId, ledger, statementId, 3, journals.Rent);

        var report = await ReportAsync(tenantId, ledger, statementId);

        report.UnmatchedStatementAmount.Should().Be(-15m);
        report.UnmatchedStatementLineCount.Should().Be(1);
        report.Difference.Should().Be(0m, "the fee explains the whole gap");
        report.IsReconciled.Should().BeFalse("the fee still needs a ledger entry before the statement is complete");
    }

    [PostgreSqlFact]
    public async Task Import_RejectsAStatementThatDoesNotFoot_AndADuplicateDate()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);

        var notFooting = async () => await WithServiceAsync(tenantId, service => service.ImportStatementAsync(
            new BankStatementImportRequest(
                ledger.CompanyId, ledger.BankAccountId, Day(31), 0m, 999m, null, [Line(2, "Deposit", 500m)]),
            Actor));
        (await notFooting.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*must equal the closing balance*");

        await ImportAsync(tenantId, ledger, closing: 500m, lines: [Line(2, "Deposit", 500m)]);
        var duplicate = async () => await ImportAsync(tenantId, ledger, closing: 500m, lines: [Line(2, "Deposit", 500m)]);
        await duplicate.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Import_RejectsGroupAccounts_AndLinesValuedAfterTheStatement()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);

        var group = async () => await WithServiceAsync(tenantId, service => service.ImportStatementAsync(
            new BankStatementImportRequest(
                ledger.CompanyId, ledger.GroupAccountId, Day(31), 0m, 5m, null, [Line(2, "Deposit", 5m)]),
            Actor));
        (await group.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*group account*");

        var valuedLater = async () => await WithServiceAsync(tenantId, service => service.ImportStatementAsync(
            new BankStatementImportRequest(
                ledger.CompanyId, ledger.BankAccountId, Day(31), 0m, 5m, null,
                [new BankStatementLineInput(Day(31).AddDays(3), "Deposit", null, 5m)]),
            Actor));
        (await valuedLater.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*after the statement date*");
    }

    [PostgreSqlFact]
    public async Task Match_RejectsAccountDateAmountAndDoubleMatchViolations()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var journals = await PostBankLinesAsync(tenantId, ledger);
        var otherAccountLine = await BankLineAfterPostingAsync(tenantId, ledger, ledger.OtherBankAccountId, ledger.EquityAccountId, 500m, Day(1));
        var laterEntry = await PostAsync(tenantId, ledger.CompanyId, new DateOnly(Day(31).Year, 2, 5), ledger.BankAccountId, ledger.RevenueAccountId, 42m);
        var laterLine = await BankLineIdAsync(tenantId, ledger.CompanyId, laterEntry, ledger.BankAccountId);
        var statementId = await ImportAsync(tenantId, ledger, closing: 720m, lines:
        [
            Line(1, "Opening deposit", 500m),
            Line(10, "Customer receipt", 300m),
            Line(20, "Rent", -80m)
        ]);

        var amountMismatch = async () => await MatchAsync(tenantId, ledger, statementId, 2, journals.Opening);
        (await amountMismatch.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*does not equal*");

        var wrongAccount = async () => await MatchAsync(tenantId, ledger, statementId, 1, otherAccountLine);
        (await wrongAccount.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*different account*");

        var postedAfter = async () => await MatchAsync(tenantId, ledger, statementId, 1, laterLine);
        (await postedAfter.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*after the statement date*");

        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);
        var twice = async () => await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);
        await twice.Should().ThrowAsync<FinancePostingConflictException>();

        await MatchAsync(tenantId, ledger, statementId, 2, journals.Sale);
        var secondStatementId = await ImportAsync(tenantId, ledger, closing: 300m, date: Day(31).AddDays(-1), lines:
        [
            Line(10, "Customer receipt again", 300m)
        ]);
        var lineOnSecond = await StatementLineIdAsync(tenantId, ledger.CompanyId, secondStatementId, 1);
        var alreadyOnOther = async () => await WithServiceAsync(tenantId, service => service.MatchLineAsync(
            ledger.CompanyId, secondStatementId, lineOnSecond, journals.Sale, Actor));
        await alreadyOnOther.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Unmatch_RequiresAReason_KeepsHistory_AndAllowsRematching()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var journals = await PostBankLinesAsync(tenantId, ledger);
        var statementId = await ImportAsync(tenantId, ledger, closing: 500m, lines: [Line(1, "Opening deposit", 500m)]);
        var lineId = await StatementLineIdAsync(tenantId, ledger.CompanyId, statementId, 1);
        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);

        var noReason = async () => await WithServiceAsync(tenantId, service => service.UnmatchLineAsync(
            ledger.CompanyId, statementId, lineId, "   ", Actor));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        await WithServiceAsync(tenantId, service => service.UnmatchLineAsync(
            ledger.CompanyId, statementId, lineId, "Matched to the wrong receipt", "reviewer"));
        var withdrawn = await WithServiceAsync(tenantId, service => service.GetStatementAsync(ledger.CompanyId, statementId));
        withdrawn!.ActiveMatchesByStatementLineId.Should().BeEmpty();

        await MatchAsync(tenantId, ledger, statementId, 1, journals.Opening);

        await using var context = _fixture.CreateContext(tenantId);
        var history = await context.BankStatementMatches.OrderBy(match => match.Id).ToListAsync(CancellationToken.None);
        history.Should().HaveCount(2);
        history[0].IsActive.Should().BeFalse();
        history[0].UnmatchReason.Should().Be("Matched to the wrong receipt");
        history[1].IsActive.Should().BeTrue();
    }

    [PostgreSqlFact]
    public async Task Statements_AreInvisibleToOtherCompaniesAndTenants()
    {
        var tenantId = $"bank-{Guid.NewGuid():N}";
        var ledger = await SetUpLedgerAsync(tenantId);
        var statementId = await ImportAsync(tenantId, ledger, closing: 500m, lines: [Line(1, "Opening deposit", 500m)]);
        var otherCompany = await CreateCompanyAsync(tenantId, "OTHER");

        var view = await WithServiceAsync(tenantId, service => service.GetStatementAsync(otherCompany, statementId));
        view.Should().BeNull();

        var otherTenant = async () => await WithServiceAsync($"bank-other-{Guid.NewGuid():N}", service =>
            service.GetReconciliationAsync(ledger.CompanyId, statementId));
        await otherTenant.Should().ThrowAsync<FinancePostingValidationException>();
    }

    private static DateOnly Day(int dayOfJanuary) => new(DateTime.UtcNow.Year, 1, dayOfJanuary);

    private static BankStatementLineInput Line(int dayOfJanuary, string description, decimal amount) =>
        new(Day(dayOfJanuary), description, null, amount);

    private static JournalPostingRequest Post(
        int companyId,
        DateOnly postingDate,
        int debitAccountId,
        int creditAccountId,
        decimal amount) => new(
        companyId,
        postingDate,
        "USD",
        "Bank movement",
        "Manual",
        $"doc-{Guid.NewGuid():N}",
        "Post",
        1,
        [
            new JournalLineInput(debitAccountId, "Bank movement", amount, 0m),
            new JournalLineInput(creditAccountId, "Bank movement", 0m, amount)
        ]);

    private async Task<Ledger> SetUpLedgerAsync(string tenantId)
    {
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var bank = await CreateAccountAsync(tenantId, companyId, "1000", "Bank", "Asset", AccountClass.Asset, group: false);
        var otherBank = await CreateAccountAsync(tenantId, companyId, "1010", "Savings", "Asset", AccountClass.Asset, group: false);
        var equity = await CreateAccountAsync(tenantId, companyId, "3000", "Opening Equity", "Equity", AccountClass.Equity, group: false);
        var revenue = await CreateAccountAsync(tenantId, companyId, "4100", "Sales", "Income", AccountClass.Income, group: false);
        var rent = await CreateAccountAsync(tenantId, companyId, "5100", "Rent", "Expense", AccountClass.Expense, group: false);
        var group = await CreateAccountAsync(tenantId, companyId, "9000", "Summary", "Summary", null, group: true);
        await WithCalendarAsync(tenantId, calendar => calendar.CreateFiscalYearAsync(
            new CreateFiscalYearRequest(
                companyId, $"FY{Day(1).Year}", new DateOnly(Day(1).Year, 1, 1), new DateOnly(Day(1).Year, 12, 31)),
            Actor));
        return new Ledger(companyId, bank, otherBank, equity, revenue, rent, group);
    }

    private sealed record Ledger(
        int CompanyId,
        int BankAccountId,
        int OtherBankAccountId,
        int EquityAccountId,
        int RevenueAccountId,
        int RentAccountId,
        int GroupAccountId);

    private sealed record BankLines(int Opening, int Sale, int Rent);

    private async Task<BankLines> PostBankLinesAsync(string tenantId, Ledger ledger)
    {
        var opening = await BankLineAfterPostingAsync(tenantId, ledger, ledger.BankAccountId, ledger.EquityAccountId, 500m, Day(1));
        var sale = await BankLineAfterPostingAsync(tenantId, ledger, ledger.BankAccountId, ledger.RevenueAccountId, 300m, Day(10));
        var rent = await BankLineAfterPostingAsync(tenantId, ledger, ledger.RentAccountId, ledger.BankAccountId, 80m, Day(20));
        return new BankLines(opening, sale, rent);
    }

    /// <summary>Posts a balanced journal and returns the bank-account line id it created.</summary>
    private async Task<int> BankLineAfterPostingAsync(
        string tenantId,
        Ledger ledger,
        int debitAccountId,
        int creditAccountId,
        decimal amount,
        DateOnly postingDate)
    {
        var entry = await PostAsync(tenantId, ledger.CompanyId, postingDate, debitAccountId, creditAccountId, amount);
        // Exactly one leg of each helper call is a bank account; that leg's line is the one under test.
        var bankAccountId = new[] { debitAccountId, creditAccountId }
            .Single(accountId => accountId == ledger.BankAccountId || accountId == ledger.OtherBankAccountId);
        return await BankLineIdAsync(tenantId, ledger.CompanyId, entry, bankAccountId);
    }

    private async Task<int> PostAsync(
        string tenantId,
        int companyId,
        DateOnly postingDate,
        int debitAccountId,
        int creditAccountId,
        decimal amount)
    {
        var result = await WithPostingAsync(tenantId, service =>
            service.PostAsync(Post(companyId, postingDate, debitAccountId, creditAccountId, amount), Actor));
        return result.JournalEntryId;
    }

    private async Task<int> BankLineIdAsync(string tenantId, int companyId, int journalEntryId, int accountId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var line = await context.JournalLines.SingleAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.JournalEntryId == journalEntryId &&
            candidate.ChartOfAccountId == accountId,
            CancellationToken.None);
        return line.Id;
    }

    private async Task<int> ImportAsync(
        string tenantId,
        Ledger ledger,
        decimal closing,
        IReadOnlyList<BankStatementLineInput> lines,
        DateOnly? date = null)
    {
        var statement = await WithServiceAsync(tenantId, service => service.ImportStatementAsync(
            new BankStatementImportRequest(
                ledger.CompanyId, ledger.BankAccountId, date ?? Day(31), 0m, closing, "STMT", lines),
            Actor));
        return statement.Id;
    }

    private async Task<int> StatementLineIdAsync(string tenantId, int companyId, int statementId, int lineNumber)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var line = await context.BankStatementLines.SingleAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.BankStatementId == statementId &&
            candidate.LineNumber == lineNumber,
            CancellationToken.None);
        return line.Id;
    }

    private async Task MatchAsync(string tenantId, Ledger ledger, int statementId, int lineNumber, int journalLineId)
    {
        var lineId = await StatementLineIdAsync(tenantId, ledger.CompanyId, statementId, lineNumber);
        await WithServiceAsync(tenantId, service => service.MatchLineAsync(
            ledger.CompanyId, statementId, lineId, journalLineId, Actor));
    }

    private Task<BankReconciliationReport> ReportAsync(string tenantId, Ledger ledger, int statementId) =>
        WithServiceAsync(tenantId, service => service.GetReconciliationAsync(ledger.CompanyId, statementId));

    private async Task<int> CreateCompanyAsync(string tenantId, string code)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var company = new Company { Code = code, LegalName = $"{code} Legal Name", BaseCurrency = "USD", IsActive = true };
        context.Companies.Add(company);
        await context.SaveChangesAsAsync(Actor);
        return company.Id;
    }

    private async Task<int> CreateAccountAsync(
        string tenantId,
        int companyId,
        string code,
        string name,
        string type,
        AccountClass? accountClass,
        bool group)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var account = new ChartOfAccount
        {
            CompanyId = companyId,
            AccountCode = code,
            Name = name,
            AccountType = type,
            IsGroupAccount = group,
            AccountClass = accountClass,
            IsActive = true
        };
        context.ChartOfAccounts.Add(account);
        await context.SaveChangesAsAsync(Actor);
        return account.Id;
    }

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<BankReconciliationService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        return await action(new BankReconciliationService(context, unitOfWork, NullLogger<BankReconciliationService>.Instance));
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
