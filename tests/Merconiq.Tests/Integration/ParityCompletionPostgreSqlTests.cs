using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Repositories;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for the parity-completion batch: project time billing (#321), payroll
/// settlement reconciliation and controlled correction (#346), advisory material proposals
/// (#326), employee self-service and safe offboarding (#338), tickets with SLA (#322), and
/// budgets versus actuals (#351). Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class ParityCompletionPostgreSqlTests
{
    private const string Actor = "parity-completion-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public ParityCompletionPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task ProjectTime_InvoicesOnce_RejectsDoubleBilling_ProfitabilityTracks()
    {
        var tenantId = $"pb-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: false);
        var (projectId, taskId) = await SeedProjectAsync(tenantId, environment.CompanyId, environment.CustomerId);

        var entry = await WithProjectServiceAsync(tenantId, service => service.LogTimeAsync(
            new LogTimeRequest(environment.CompanyId, taskId, DateOnly.FromDateTime(DateTime.UtcNow), 8m,
                Billable: true, HourlyRate: 60m), "worker-1"));
        await WithProjectServiceAsync(tenantId, service => service.SubmitTimeAsync(
            environment.CompanyId, entry.Id, "worker-1"));
        await WithProjectServiceAsync(tenantId, service => service.ApproveTimeAsync(
            environment.CompanyId, entry.Id, Actor));

        var invoice = await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                environment.CompanyId, environment.CustomerId, "SVC-INV-1",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "pb-invoice", [], TimeLines: [new PostSalesInvoiceTimeLine(entry.Id)]), Actor));

        invoice.TotalValue.Should().Be(480m, "8 hours at 60.00");
        invoice.OpenValue.Should().Be(480m);
        invoice.JournalNumber.Should().StartWith("JV");

        var doubleBill = () => WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                environment.CompanyId, environment.CustomerId, "SVC-INV-2",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "pb-invoice-2", [], TimeLines: [new PostSalesInvoiceTimeLine(entry.Id)]), Actor));
        (await doubleBill.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already billed*");

        var unapproved = await WithProjectServiceAsync(tenantId, service => service.LogTimeAsync(
            new LogTimeRequest(environment.CompanyId, taskId, DateOnly.FromDateTime(DateTime.UtcNow), 2m,
                Billable: true, HourlyRate: 60m), "worker-1"));
        var notApproved = () => WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                environment.CompanyId, environment.CustomerId, "SVC-INV-3",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "pb-invoice-3", [], TimeLines: [new PostSalesInvoiceTimeLine(unapproved.Id)]), Actor));
        (await notApproved.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*approved and billable*");

        var profitability = await WithProjectServiceAsync(tenantId, service =>
            service.GetProfitabilityAsync(environment.CompanyId, projectId));
        profitability.InvoicedValue.Should().Be(480m);
        profitability.UninvoicedBillableValue.Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task PayRun_SettlementReconciles_AndControlledReversalWorks()
    {
        var tenantId = $"pc-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: true);
        var period = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        await using (var hrContext = _fixture.CreateContext(tenantId))
        {
            var hr = BuildHrService(hrContext);
            var employee = await hr.CreateEmployeeAsync(
                new CreateEmployeeRequest(environment.CompanyId, "EMP-1", "Payroll Person",
                    DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)), Actor);
            await BuildPayrollService(hrContext).SetSalaryStructureAsync(
                new SetSalaryStructureRequest(environment.CompanyId, employee.Id, period, 3000m, 200m), Actor);
        }

        var run = await WithPayrollServiceAsync(tenantId, service =>
            service.DraftPayRunAsync(environment.CompanyId, period, Actor));
        await WithPayrollServiceAsync(tenantId, service =>
            service.PostPayRunAsync(environment.CompanyId, run.Id, Actor));

        var settlement = await WithPayrollServiceAsync(tenantId, service =>
            service.GetSettlementReportAsync(environment.CompanyId, run.Id));
        settlement.Reconciled.Should().BeTrue();
        settlement.PostedGross.Should().Be(settlement.DeclaredGross).And.Be(3200m);
        settlement.PostedNet.Should().Be(settlement.DeclaredNet).And.Be(3200m);
        settlement.Reversible.Should().BeTrue();

        var reversed = await WithPayrollServiceAsync(tenantId, service =>
            service.ReversePayRunAsync(environment.CompanyId, run.Id, "Duplicate payroll", Actor));
        reversed.Status.Should().Be(PayRunStatus.Reversed);
        reversed.ReversalReason.Should().Be("Duplicate payroll");

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        trialBalance.Rows.Single(row => row.AccountCode == "6000").Should().Match<TrialBalanceRow>(
            row => row.Debit == 3200m && row.Credit == 3200m, "expense posted then reversed");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        var afterReversal = await WithPayrollServiceAsync(tenantId, service =>
            service.GetSettlementReportAsync(environment.CompanyId, run.Id));
        afterReversal.Reconciled.Should().BeTrue();
        afterReversal.Reversible.Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task MaterialProposal_ExplainsShortage_AdvisoryOnly()
    {
        var tenantId = $"mp-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: false);
        var bom = await WithManufacturingServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 1,
                [new BomLineInput(environment.ComponentAId, 2)]), Actor));
        var workOrder = await WithManufacturingServiceAsync(tenantId, service => service.CreateWorkOrderAsync(
            new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 20, environment.LocationId), Actor));

        // 30 on hand, need 40 → shortage of 10. Proposal commits nothing.
        var proposal = await WithManufacturingServiceAsync(tenantId, service =>
            service.GetMaterialProposalAsync(environment.CompanyId, workOrder.Id));
        proposal.Proceed.Should().BeFalse();
        proposal.Lines.Single().Should().Match<MaterialProposalLine>(line =>
            line.RequiredQuantity == 40m && line.OnHandQuantity == 30m &&
            line.AvailableQuantity == 30m && line.ShortageQuantity == 10m &&
            line.Verdict == "Shortage");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(30,
            "the proposal is advisory and consumes nothing");
    }

    [PostgreSqlFact]
    public async Task SelfService_ResolvesOwnRecord_AndOffboarding_GuardsOpenState()
    {
        var tenantId = $"ss-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: false);
        var userId = $"user-{Guid.NewGuid():N}";
        var employee = await WithHrServiceAsync(tenantId, service => service.CreateEmployeeAsync(
            new CreateEmployeeRequest(environment.CompanyId, "EMP-SS", "Self Service Person",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), UserId: userId), Actor));

        var me = await WithHrServiceAsync(tenantId, service =>
            service.GetMyProfileAsync(environment.CompanyId, userId));
        me.Should().NotBeNull();
        me!.EmployeeId.Should().Be(employee.Id);

        // Open attendance blocks offboarding.
        await WithHrServiceAsync(tenantId, service => service.CheckInAsync(
            environment.CompanyId, employee.Id, null, Actor));
        var blocked = () => WithHrServiceAsync(tenantId, service => service.DeactivateEmployeeAsync(
            environment.CompanyId, employee.Id, "Resignation", Actor));
        (await blocked.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*open attendance*");

        await WithHrServiceAsync(tenantId, service => service.CheckOutAsync(
            environment.CompanyId, employee.Id, Actor));
        var offboarded = await WithHrServiceAsync(tenantId, service => service.DeactivateEmployeeAsync(
            environment.CompanyId, employee.Id, "Resignation", Actor));
        offboarded.IsActive.Should().BeFalse();

        var afterOffboarding = await WithHrServiceAsync(tenantId, service =>
            service.GetMyProfileAsync(environment.CompanyId, userId));
        afterOffboarding!.IsActive.Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task Tickets_SlaClockAndBreachReport_Work()
    {
        var tenantId = $"tk-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: false);
        var ticket = await WithTicketServiceAsync(tenantId, service => service.CreateTicketAsync(
            new CreateTicketRequest(environment.CompanyId, "Login broken", null, TicketPriority.Critical), Actor));

        ticket.ResolutionDueAtUtc.Should().NotBeNull();
        (ticket.ResolutionDueAtUtc!.Value - ticket.CreatedAtUtc).Should().Be(TimeSpan.FromHours(4),
            "critical priority carries the 4-hour SLA");

        var beforeResolution = await WithTicketServiceAsync(tenantId, service =>
            service.ListBreachedAsync(environment.CompanyId));
        beforeResolution.Should().BeEmpty("the SLA clock has not expired yet");

        await WithTicketServiceAsync(tenantId, service => service.StartTicketAsync(
            environment.CompanyId, ticket.Id, Actor));
        await WithTicketServiceAsync(tenantId, service => service.ResolveTicketAsync(
            environment.CompanyId, ticket.Id, Actor));
        var closed = await WithTicketServiceAsync(tenantId, service => service.CloseTicketAsync(
            environment.CompanyId, ticket.Id, Actor));
        closed.Status.Should().Be(TicketStatus.Closed);
    }

    [PostgreSqlFact]
    public async Task Budgets_ComparePostedActuals()
    {
        var tenantId = $"bd-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, withPayroll: true);
        var period = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        await using (var hrContext = _fixture.CreateContext(tenantId))
        {
            var hr = BuildHrService(hrContext);
            var employee = await hr.CreateEmployeeAsync(
                new CreateEmployeeRequest(environment.CompanyId, "EMP-B", "Budget Person",
                    DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)), Actor);
            var payroll = BuildPayrollService(hrContext);
            await payroll.SetSalaryStructureAsync(
                new SetSalaryStructureRequest(environment.CompanyId, employee.Id, period, 1200m, 0m), Actor);
            var run = await payroll.DraftPayRunAsync(environment.CompanyId, period, Actor);
            await payroll.PostPayRunAsync(environment.CompanyId, run.Id, Actor);
        }

        var salaryAccount = await _fixture.CreateContext(tenantId).ChartOfAccounts.SingleAsync(
            account => account.CompanyId == environment.CompanyId && account.AccountCode == "6000");
        var budget = await WithBudgetServiceAsync(tenantId, service => service.SetBudgetAsync(
            new SetBudgetRequest(environment.CompanyId, salaryAccount.Id, DateTime.UtcNow.Year, 1000m), Actor));

        var versus = await WithBudgetServiceAsync(tenantId, service =>
            service.GetBudgetVersusActualAsync(environment.CompanyId, budget.Id));
        versus.BudgetedAmount.Should().Be(1000m);
        versus.ActualAmount.Should().Be(1200m, "one posted payroll of 1,200");
        versus.OverBudget.Should().BeTrue();
        versus.Variance.Should().Be(200m);
    }

    private static ProjectService BuildProjectService(InventoryDbContext context) =>
        new(context, new UnitOfWork(context), NullLogger<ProjectService>.Instance);

    private static HumanResourcesService BuildHrService(InventoryDbContext context) =>
        new(context, new UnitOfWork(context), NullLogger<HumanResourcesService>.Instance);

    private static PayrollService BuildPayrollService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PayrollService(
            context, unitOfWork,
            new JournalPostingService(context, unitOfWork, fiscalCalendar,
                new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PayrollService>.Instance);
    }

    private static SalesService BuildSalesService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var webhookDispatcher = new Mock<IWebhookDispatcher>();
        webhookDispatcher
            .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var stockService = new StockService(
            new Repository<StockInHand>(context),
            new Repository<StockTransaction>(context),
            new Repository<Item>(context),
            new Repository<Location>(context),
            new Repository<Branch>(context),
            unitOfWork,
            webhookDispatcher.Object,
            new TestTenantContext(context.CurrentTenantId),
            NullLogger<StockService>.Instance,
            new Repository<StockValuationBucket>(context),
            new Repository<StockValuationEntry>(context));
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        return new SalesService(
            context, unitOfWork, stockService,
            new JournalPostingService(context, unitOfWork, fiscalCalendar, documentNumbers,
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<SalesService>.Instance);
    }

    private static ManufacturingService BuildManufacturingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var webhookDispatcher = new Mock<IWebhookDispatcher>();
        webhookDispatcher
            .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var stockService = new StockService(
            new Repository<StockInHand>(context),
            new Repository<StockTransaction>(context),
            new Repository<Item>(context),
            new Repository<Location>(context),
            new Repository<Branch>(context),
            unitOfWork,
            webhookDispatcher.Object,
            new TestTenantContext(context.CurrentTenantId),
            NullLogger<StockService>.Instance,
            new Repository<StockValuationBucket>(context),
            new Repository<StockValuationEntry>(context));
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new ManufacturingService(
            context, unitOfWork, stockService,
            new JournalPostingService(context, unitOfWork, fiscalCalendar,
                new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            new DocumentNumberService(context, unitOfWork),
            NullLogger<ManufacturingService>.Instance);
    }

    private static TicketService BuildTicketService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        return new TicketService(context, unitOfWork,
            new DocumentNumberService(context, unitOfWork), NullLogger<TicketService>.Instance);
    }

    private static BudgetService BuildBudgetService(InventoryDbContext context) => new(context, new UnitOfWork(context));

    private async Task<T> WithSalesServiceAsync<T>(string tenantId, Func<SalesService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildSalesService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithProjectServiceAsync<T>(string tenantId, Func<ProjectService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildProjectService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithHrServiceAsync<T>(string tenantId, Func<HumanResourcesService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildHrService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithPayrollServiceAsync<T>(string tenantId, Func<PayrollService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildPayrollService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithManufacturingServiceAsync<T>(string tenantId, Func<ManufacturingService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildManufacturingService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithTicketServiceAsync<T>(string tenantId, Func<TicketService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildTicketService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<T> WithBudgetServiceAsync<T>(string tenantId, Func<BudgetService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try { return await action(BuildBudgetService(context)); }
        finally { await context.DisposeAsync(); }
    }

    private async Task<TrialBalanceResult> TrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var posting = new JournalPostingService(context, unitOfWork, fiscalCalendar,
            new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance);
        return await posting.GetTrialBalanceAsync(companyId, null, null);
    }

    private async Task<(int ProjectId, int TaskId)> SeedProjectAsync(
        string tenantId, int companyId, int customerId)
    {
        var project = await WithProjectServiceAsync(tenantId, service => service.CreateProjectAsync(
            new CreateProjectRequest(companyId, "PRJ-BILL", "Billing project", customerId), Actor));
        var task = await WithProjectServiceAsync(tenantId, service => service.CreateTaskAsync(
            new CreateProjectTaskRequest(companyId, project.Id, "Consulting"), Actor));
        return (project.Id, task.Id);
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(string tenantId, bool withPayroll)
    {
        int companyId;
        int customerId;
        int locationId;
        int finishedItemId;
        int componentAId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "PC-MAIN", LegalName = "Parity completion company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "PC-BR", Name = "HQ", TimeZoneId = "UTC" };
            var customer = new Customer
            {
                CustomerCode = $"CUST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                Name = "PC customer", IsActive = true
            };
            var finished = new Item
            {
                ItemCode = $"FIN-{Guid.NewGuid():N}"[..20], Description = "Finished",
                Rate = 30m, IsActive = true
            };
            var componentA = new Item
            {
                ItemCode = $"CMP-{Guid.NewGuid():N}"[..20], Description = "Component",
                Rate = 4m, IsActive = true
            };
            var location = new Location { Name = "Main warehouse" };
            branch.Company = company;
            customer.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, customer, finished, componentA, location);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            customerId = customer.Id;
            locationId = location.Id;
            finishedItemId = finished.Id;
            componentAId = componentA.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1100", Name = "Accounts Receivable", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "3000", Name = "Opening Equity", AccountType = "Equity", IsGroupAccount = false, AccountClass = AccountClass.Equity, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", IsGroupAccount = false, AccountClass = AccountClass.Income, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5000", Name = "Cost of Goods Sold", AccountType = "Expense", IsGroupAccount = false, AccountClass = AccountClass.Expense, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "6000", Name = "Salary Expense", AccountType = "Expense", IsGroupAccount = false, AccountClass = AccountClass.Expense, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsReceivable, ChartOfAccountId = ByCode("1100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalesRevenue, ChartOfAccountId = ByCode("4000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CostOfGoodsSold, ChartOfAccountId = ByCode("5000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalaryExpense, ChartOfAccountId = ByCode("6000").Id, IsActive = true });
            await setup.SaveChangesAsAsync(Actor);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var calendarContext = _fixture.CreateContext(tenantId))
        {
            var calendar = new FiscalCalendarService(
                calendarContext, new UnitOfWork(calendarContext), NullLogger<FiscalCalendarService>.Instance);
            await calendar.CreateFiscalYearAsync(
                new CreateFiscalYearRequest(
                    companyId, $"FY{today.Year}", new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
                Actor);
        }

        if (!withPayroll)
        {
            // Opening stock for the material-proposal test.
            await using (var stockContext = _fixture.CreateContext(tenantId))
            {
                var unitOfWork = new UnitOfWork(stockContext);
                var webhookDispatcher = new Mock<IWebhookDispatcher>();
                webhookDispatcher
                    .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var stockService = new StockService(
                    new Repository<StockInHand>(stockContext),
                    new Repository<StockTransaction>(stockContext),
                    new Repository<Item>(stockContext),
                    new Repository<Location>(stockContext),
                    new Repository<Branch>(stockContext),
                    unitOfWork,
                    webhookDispatcher.Object,
                    new TestTenantContext(stockContext.CurrentTenantId),
                    NullLogger<StockService>.Instance,
                    new Repository<StockValuationBucket>(stockContext),
                    new Repository<StockValuationEntry>(stockContext));
                await stockService.ReceiveStockAsync(
                    componentAId, locationId, 30, notes: "opening", unitCost: 4m,
                    mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
            }
        }

        return new TestEnvironment
        {
            CompanyId = companyId,
            CustomerId = customerId,
            LocationId = locationId,
            FinishedItemId = finishedItemId,
            ComponentAId = componentAId,
            MutationScope = new StockMutationScope(companyId, () => Task.FromResult(true))
        };
    }

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int CustomerId { get; init; }
        public int LocationId { get; init; }
        public int FinishedItemId { get; init; }
        public int ComponentAId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }
}
