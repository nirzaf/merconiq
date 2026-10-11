using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for account-hierarchy rollups in the income statement and balance sheet.
/// Group subtotals must sum the posted, same-class leaves beneath them at every depth, stay scoped
/// to one company, and never change the statement totals they summarize. Requires real PostgreSQL
/// semantics and is skipped unless RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class FinanceStatementRollupsPostgreSqlTests
{
    private const string Actor = "statement-rollup-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public FinanceStatementRollupsPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task ProfitAndLoss_RollsNestedGroupsIntoEveryAncestorWithDepth()
    {
        var tenantId = $"rollup-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var accounts = await CreateChartAsync(tenantId, companyId);
        await CreateFiscalYearAsync(tenantId, companyId);
        var today = Today();

        await PostAsync(tenantId, companyId, today, "Opening equity", "OPEN",
            Line(accounts["1000"], 500m, 0m), Line(accounts["3000"], 0m, 500m));
        await PostAsync(tenantId, companyId, today, "Product sale", "SALE-1",
            Line(accounts["1000"], 300m, 0m), Line(accounts["4100"], 0m, 300m));
        await PostAsync(tenantId, companyId, today, "Consulting sale", "SALE-2",
            Line(accounts["1000"], 200m, 0m), Line(accounts["4210"], 0m, 200m));
        await PostAsync(tenantId, companyId, today, "Rent", "RENT-1",
            Line(accounts["5100"], 80m, 0m), Line(accounts["1000"], 0m, 80m));

        var statement = await WithStatementsAsync(tenantId, service => service.GetProfitAndLossAsync(companyId, today, today));

        statement.TotalIncome.Should().Be(500m);
        statement.TotalExpenses.Should().Be(80m);
        statement.NetProfit.Should().Be(420m);
        statement.Rollups.Select(line => (line.AccountCode, line.Depth, line.Amount)).Should().Equal(
            ("4000", 0, 500m),
            ("4200", 1, 200m),
            ("5000", 0, 80m));
        statement.Rollups.Single(line => line.AccountCode == "4200").ParentChartOfAccountId
            .Should().Be(accounts["4000"]);
    }

    [PostgreSqlFact]
    public async Task BalanceSheet_RollsAssetGroupsAndBalancesWithRetainedProfit()
    {
        var tenantId = $"rollup-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var accounts = await CreateChartAsync(tenantId, companyId);
        await CreateFiscalYearAsync(tenantId, companyId);
        var today = Today();

        await PostAsync(tenantId, companyId, today, "Opening equity", "OPEN",
            Line(accounts["1000"], 500m, 0m), Line(accounts["3000"], 0m, 500m));
        await PostAsync(tenantId, companyId, today, "Product sale", "SALE-1",
            Line(accounts["1000"], 300m, 0m), Line(accounts["4100"], 0m, 300m));
        await PostAsync(tenantId, companyId, today, "Rent", "RENT-1",
            Line(accounts["5100"], 80m, 0m), Line(accounts["1000"], 0m, 80m));

        var statement = await WithStatementsAsync(tenantId, service => service.GetBalanceSheetAsync(companyId, today));

        statement.Balances.Should().BeTrue();
        statement.TotalAssets.Should().Be(720m);
        statement.TotalEquity.Should().Be(500m);
        statement.NetProfit.Should().Be(220m);
        statement.Rollups.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new StatementRollupLine(
                accounts["1900"], "1900", "Current Assets", AccountClass.Asset, null, 0, 720m));
    }

    [PostgreSqlFact]
    public async Task Rollups_NeverIncludeAnotherCompanysPostingsOrAccounts()
    {
        var tenantId = $"rollup-{Guid.NewGuid():N}";
        var companyA = await CreateCompanyAsync(tenantId, "ALPHA");
        var companyB = await CreateCompanyAsync(tenantId, "BETA");
        var chartA = await CreateChartAsync(tenantId, companyA);
        var chartB = await CreateChartAsync(tenantId, companyB);
        await CreateFiscalYearAsync(tenantId, companyA);
        await CreateFiscalYearAsync(tenantId, companyB);
        var today = Today();

        await PostAsync(tenantId, companyA, today, "Sale A", "SALE-A",
            Line(chartA["1000"], 100m, 0m), Line(chartA["4100"], 0m, 100m));
        await PostAsync(tenantId, companyB, today, "Sale B", "SALE-B",
            Line(chartB["1000"], 999m, 0m), Line(chartB["4100"], 0m, 999m));

        var statementA = await WithStatementsAsync(tenantId, service => service.GetProfitAndLossAsync(companyA, today, today));

        statementA.Rollups.Single(line => line.AccountCode == "4000").Amount.Should().Be(100m);
        statementA.Rollups.Should().OnlyContain(line => line.ChartOfAccountId != chartB["4000"]);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static JournalLineInput Line(int accountId, decimal debit, decimal credit) =>
        new(accountId, null, debit, credit);

    private async Task PostAsync(
        string tenantId,
        int companyId,
        DateOnly postingDate,
        string description,
        string sourceDocumentId,
        params JournalLineInput[] lines)
    {
        var request = new JournalPostingRequest(
            companyId, postingDate, "USD", description, "Manual", sourceDocumentId, "Post", 1, lines);
        await WithPostingAsync(tenantId, service => service.PostAsync(request, Actor));
    }

    /// <summary>
    /// Builds a classified chart with one nested income group, one nested expense group, a bank
    /// group, and leaves. Parents are attached through navigation properties so EF orders the inserts.
    /// </summary>
    private async Task<Dictionary<string, int>> CreateChartAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var entities = new Dictionary<string, ChartOfAccount>();

        ChartOfAccount Add(string code, string name, AccountClass accountClass, bool isGroup, string? parentCode)
        {
            var account = new ChartOfAccount
            {
                CompanyId = companyId,
                AccountCode = code,
                Name = name,
                AccountType = isGroup ? "Summary" : "Ledger",
                IsGroupAccount = isGroup,
                AccountClass = accountClass,
                IsActive = true,
                ParentAccount = parentCode is null ? null : entities[parentCode]
            };
            entities[code] = account;
            context.ChartOfAccounts.Add(account);
            return account;
        }

        Add("1900", "Current Assets", AccountClass.Asset, isGroup: true, parentCode: null);
        Add("1000", "Bank", AccountClass.Asset, isGroup: false, parentCode: "1900");
        Add("3000", "Opening Equity", AccountClass.Equity, isGroup: false, parentCode: null);
        Add("4000", "Revenue", AccountClass.Income, isGroup: true, parentCode: null);
        Add("4100", "Product Sales", AccountClass.Income, isGroup: false, parentCode: "4000");
        Add("4200", "Service Sales", AccountClass.Income, isGroup: true, parentCode: "4000");
        Add("4210", "Consulting", AccountClass.Income, isGroup: false, parentCode: "4200");
        Add("5000", "Operating Expenses", AccountClass.Expense, isGroup: true, parentCode: null);
        Add("5100", "Rent", AccountClass.Expense, isGroup: false, parentCode: "5000");

        await context.SaveChangesAsAsync(Actor);
        return entities.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
    }

    private async Task<int> CreateCompanyAsync(string tenantId, string code)
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

    private async Task CreateFiscalYearAsync(string tenantId, int companyId)
    {
        var year = Today().Year;
        await WithFiscalCalendarAsync(tenantId, calendar => calendar.CreateFiscalYearAsync(
            new CreateFiscalYearRequest(companyId, $"FY{year}", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)),
            Actor));
    }

    private async Task<T> WithPostingAsync<T>(string tenantId, Func<JournalPostingService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            var unitOfWork = new UnitOfWork(context);
            var service = new JournalPostingService(
                context,
                unitOfWork,
                new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance),
                new DocumentNumberService(context, unitOfWork),
                NullLogger<JournalPostingService>.Instance);
            return await action(service);
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
            var unitOfWork = new UnitOfWork(context);
            return await action(new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<T> WithStatementsAsync<T>(string tenantId, Func<FinanceStatementService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(new FinanceStatementService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
