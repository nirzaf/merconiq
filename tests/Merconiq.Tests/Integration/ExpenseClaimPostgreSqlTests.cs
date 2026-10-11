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
/// Relational acceptance for expense claims: approval posts each expense against the employee payable,
/// payment settles that payable from the bank, rejection is final, and every transition is guarded. Requires
/// real PostgreSQL semantics and is skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class ExpenseClaimPostgreSqlTests
{
    private const string Actor = "expense-claim-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public ExpenseClaimPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Approve_PostsEachExpenseAgainstTheEmployeePayable_AndStaysBalanced()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: true);
        var claim = await CreateAndSubmitAsync(tenantId, company);

        var approved = await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, claim.Id, Actor));

        approved.Status.Should().Be(ExpenseClaimStatus.Approved);
        approved.ClaimNumber.Should().StartWith("EC");
        approved.ApprovalJournalEntryId.Should().NotBeNull();
        var lines = await JournalLinesAsync(tenantId, approved.ApprovalJournalEntryId!.Value);
        lines.Single(line => line.ChartOfAccountId == company.Accounts["6100"]).Debit.Should().Be(40m);
        lines.Single(line => line.ChartOfAccountId == company.Accounts["6200"]).Debit.Should().Be(10m);
        lines.Single(line => line.ChartOfAccountId == company.Accounts["2300"]).Credit.Should().Be(50m);
        lines.Sum(line => line.Debit).Should().Be(lines.Sum(line => line.Credit));

        var again = async () => await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, claim.Id, Actor));
        await again.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Payment_SettlesThePayableFromTheBank()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: true);
        var claim = await CreateAndSubmitAsync(tenantId, company);
        await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, claim.Id, Actor));

        var paid = await WithClaimsAsync(tenantId, service => service.PayAsync(
            company.CompanyId, claim.Id, company.Accounts["1000"], Today(), Actor));

        paid.Status.Should().Be(ExpenseClaimStatus.Paid);
        paid.PaymentDate.Should().Be(Today());
        var lines = await JournalLinesAsync(tenantId, paid.PaymentJournalEntryId!.Value);
        lines.Single(line => line.ChartOfAccountId == company.Accounts["2300"]).Debit.Should().Be(50m);
        lines.Single(line => line.ChartOfAccountId == company.Accounts["1000"]).Credit.Should().Be(50m);

        var trialBalance = await WithPostingAsync(tenantId, service => service.GetTrialBalanceAsync(company.CompanyId, null, null));
        var payable = trialBalance.Rows.Single(row => row.AccountCode == "2300");
        (payable.Credit - payable.Debit).Should().Be(0m, "the payable is fully settled");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        var repay = async () => await WithClaimsAsync(tenantId, service => service.PayAsync(
            company.CompanyId, claim.Id, company.Accounts["1000"], Today(), Actor));
        await repay.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Lifecycle_IsGuarded_RejectionIsFinal_AndReasonsAreRequired()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: true);
        var draft = await CreateClaimAsync(tenantId, company);

        var approveDraft = async () => await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, draft.Id, Actor));
        await approveDraft.Should().ThrowAsync<FinancePostingConflictException>();

        await WithClaimsAsync(tenantId, service => service.SubmitAsync(company.CompanyId, draft.Id, Actor));
        var noReason = async () => await WithClaimsAsync(tenantId, service => service.RejectAsync(company.CompanyId, draft.Id, "  ", Actor));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        var rejected = await WithClaimsAsync(tenantId, service => service.RejectAsync(
            company.CompanyId, draft.Id, "Receipts missing for meals", "approver"));
        rejected.Status.Should().Be(ExpenseClaimStatus.Rejected);
        rejected.RejectionReason.Should().Be("Receipts missing for meals");

        var approveRejected = async () => await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, draft.Id, Actor));
        await approveRejected.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Creation_RejectsGroupInactiveAndNonPositiveLines()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: true);

        var group = async () => await CreateClaimAsync(tenantId, company, lines: [(company.Accounts["9000"], 5m)]);
        (await group.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*group account*");

        var inactive = async () => await CreateClaimAsync(tenantId, company, lines: [(company.Accounts["6900"], 5m)]);
        (await inactive.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*inactive*");

        var zero = async () => await CreateClaimAsync(tenantId, company, lines: [(company.Accounts["6100"], 0m)]);
        await zero.Should().ThrowAsync<FinancePostingValidationException>();

        var none = async () => await CreateClaimAsync(tenantId, company, lines: []);
        await none.Should().ThrowAsync<FinancePostingValidationException>();
    }

    [PostgreSqlFact]
    public async Task Approval_WithoutAPayableMapping_FailsAndPostsNothing()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: false);
        var claim = await CreateAndSubmitAsync(tenantId, company);

        var approve = async () => await WithClaimsAsync(tenantId, service => service.ApproveAsync(company.CompanyId, claim.Id, Actor));

        (await approve.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*EmployeeExpensesPayable*");
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.JournalEntries.CountAsync(CancellationToken.None)).Should().Be(0);
        (await verify.ExpenseClaims.SingleAsync(candidate => candidate.Id == claim.Id, CancellationToken.None))
            .Status.Should().Be(ExpenseClaimStatus.Submitted);
    }

    [PostgreSqlFact]
    public async Task Claims_AreInvisibleToOtherCompanies()
    {
        var tenantId = $"exp-{Guid.NewGuid():N}";
        var company = await SetUpCompanyAsync(tenantId, mapPayable: true);
        var claim = await CreateClaimAsync(tenantId, company);
        var otherCompany = await CreateBareCompanyAsync(tenantId, "OTHER");

        var view = await WithClaimsAsync(tenantId, service => service.GetAsync(otherCompany, claim.Id));

        view.Should().BeNull();
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record CompanySetup(int CompanyId, Dictionary<string, int> Accounts);

    private async Task<CompanySetup> SetUpCompanyAsync(string tenantId, bool mapPayable)
    {
        var companyId = await CreateBareCompanyAsync(tenantId, "MAIN");
        var accounts = new Dictionary<string, int>();
        await using (var context = _fixture.CreateContext(tenantId))
        {
            var definitions = new[]
            {
                ("1000", "Bank", "Asset", AccountClass.Asset, false, true),
                ("2300", "Employee Payable", "Liability", AccountClass.Liability, false, true),
                ("6100", "Travel", "Expense", AccountClass.Expense, false, true),
                ("6200", "Meals", "Expense", AccountClass.Expense, false, true),
                ("6900", "Retired expense", "Expense", AccountClass.Expense, false, false),
                ("9000", "Summary", "Summary", (AccountClass?)null, true, true)
            };
            foreach (var (code, name, type, accountClass, isGroup, isActive) in definitions)
            {
                var account = new ChartOfAccount
                {
                    CompanyId = companyId,
                    AccountCode = code,
                    Name = name,
                    AccountType = type,
                    AccountClass = accountClass,
                    IsGroupAccount = isGroup,
                    IsActive = isActive
                };
                context.ChartOfAccounts.Add(account);
                await context.SaveChangesAsAsync(Actor);
                accounts[code] = account.Id;
            }

            if (mapPayable)
            {
                context.CompanyAccountMappings.Add(new CompanyAccountMapping
                {
                    CompanyId = companyId,
                    Role = AccountPostingRole.EmployeeExpensesPayable,
                    ChartOfAccountId = accounts["2300"],
                    IsActive = true
                });
                await context.SaveChangesAsAsync(Actor);
            }
        }

        await using (var context = _fixture.CreateContext(tenantId))
        {
            var year = Today().Year;
            await new FiscalCalendarService(context, new UnitOfWork(context), NullLogger<FiscalCalendarService>.Instance)
                .CreateFiscalYearAsync(
                    new CreateFiscalYearRequest(companyId, $"FY{year}", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
                    Actor);
        }

        return new CompanySetup(companyId, accounts);
    }

    private async Task<ExpenseClaim> CreateAndSubmitAsync(string tenantId, CompanySetup company)
    {
        var claim = await CreateClaimAsync(tenantId, company);
        return await WithClaimsAsync(tenantId, service => service.SubmitAsync(company.CompanyId, claim.Id, Actor));
    }

    private async Task<ExpenseClaim> CreateClaimAsync(
        string tenantId,
        CompanySetup company,
        IReadOnlyList<(int AccountId, decimal Amount)>? lines = null)
    {
        var inputs = lines?.Select(line => new ExpenseClaimLineInput(line.AccountId, "Expense", line.Amount)).ToArray()
            ?? [
                new ExpenseClaimLineInput(company.Accounts["6100"], "Taxi to client site", 40m),
                new ExpenseClaimLineInput(company.Accounts["6200"], "Client lunch", 10m)
            ];
        return await WithClaimsAsync(tenantId, service => service.CreateAsync(
            new CreateExpenseClaimRequest(company.CompanyId, "Nadia Perera", Today(), "Client visit", inputs), Actor));
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

    private async Task<T> WithClaimsAsync<T>(string tenantId, Func<ExpenseClaimService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var journal = BuildJournal(context, unitOfWork);
        return await action(new ExpenseClaimService(
            context,
            unitOfWork,
            journal,
            new AccountPostingPolicyService(context),
            new DocumentNumberService(context, unitOfWork),
            NullLogger<ExpenseClaimService>.Instance));
    }

    private async Task<T> WithPostingAsync<T>(string tenantId, Func<JournalPostingService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await action(BuildJournal(context, new UnitOfWork(context)));
    }

    private static JournalPostingService BuildJournal(InventoryDbContext context, UnitOfWork unitOfWork) =>
        new(
            context,
            unitOfWork,
            new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance),
            new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
}
