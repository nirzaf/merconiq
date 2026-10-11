using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Merconiq.Infrastructure.Repositories;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for projects/timesheets, HR, and payroll (with its GL journal through
/// the finance kernel). Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class ProjectsHrPayrollPostgreSqlTests
{
    private const string Actor = "hr-payroll-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public ProjectsHrPayrollPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Timesheet_ApprovalFreezesEvidence_AndProfitabilityReadsApprovedOnly()
    {
        var tenantId = $"prj-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        int taskId = 0;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var project = await WithServiceAsync(tenantId, service => service.CreateProjectAsync(
                new CreateProjectRequest(environment.CompanyId, "PRJ-1", "Fleet portal"), Actor));
            var task = await WithServiceAsync(tenantId, service => service.CreateTaskAsync(
                new CreateProjectTaskRequest(environment.CompanyId, project.Id, "Design API"), Actor));
            taskId = task.Id;
        }

        var draft = await WithServiceAsync(tenantId, service => service.LogTimeAsync(
            new LogTimeRequest(environment.CompanyId, taskId, DateOnly.FromDateTime(DateTime.UtcNow), 8m,
                Billable: true, HourlyRate: 50m), "worker-1"));
        var submitted = await WithServiceAsync(tenantId, service => service.SubmitTimeAsync(
            environment.CompanyId, draft.Id, "worker-1"));
        var approved = await WithServiceAsync(tenantId, service => service.ApproveTimeAsync(
            environment.CompanyId, submitted.Id, Actor));

        approved.Status.Should().Be(TimesheetEntryStatus.Approved);
        approved.ApprovedBy.Should().Be(Actor);

        // A second submitted line stays out of profitability until approved.
        var second = await WithServiceAsync(tenantId, service => service.LogTimeAsync(
            new LogTimeRequest(environment.CompanyId, taskId, DateOnly.FromDateTime(DateTime.UtcNow), 4m,
                Billable: true, HourlyRate: 50m), "worker-2"));
        await WithServiceAsync(tenantId, service => service.SubmitTimeAsync(
            environment.CompanyId, second.Id, "worker-2"));

        var rejected = await WithServiceAsync(tenantId, service => service.RejectTimeAsync(
            environment.CompanyId, second.Id, "Wrong task", Actor));
        rejected.Status.Should().Be(TimesheetEntryStatus.Rejected);

        var profitability = await WithServiceAsync(tenantId, service =>
            service.GetProfitabilityAsync(environment.CompanyId, draft.ProjectId));
        profitability.ApprovedHours.Should().Be(8m);
        profitability.BillableValue.Should().Be(400m);
        profitability.RejectedEntries.Should().Be(1);

        // Approved evidence is frozen: resubmission of an approved line conflicts.
        var resubmit = () => WithServiceAsync(tenantId, service => service.SubmitTimeAsync(
            environment.CompanyId, approved.Id, "worker-1"));
        (await resubmit.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*Approved*cannot be submitted*");
    }

    [PostgreSqlFact]
    public async Task Leave_OverlapApprovalRejected_BalanceCountsApproved()
    {
        var tenantId = $"hr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var employee = await WithHrServiceAsync(tenantId, service => service.CreateEmployeeAsync(
            new CreateEmployeeRequest(environment.CompanyId, "EMP-1", "Aisha Rahman",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)), Actor));

        var first = await WithHrServiceAsync(tenantId, service => service.RequestLeaveAsync(
            new RequestLeaveRequest(environment.CompanyId, employee.Id, LeaveType.Annual,
                new DateOnly(DateTime.UtcNow.Year, 12, 1), new DateOnly(DateTime.UtcNow.Year, 12, 5),
                "Family event"), Actor));

        // A second overlapping request can be created but approving both is impossible.
        var second = await WithHrServiceAsync(tenantId, service => service.RequestLeaveAsync(
            new RequestLeaveRequest(environment.CompanyId, employee.Id, LeaveType.Annual,
                new DateOnly(DateTime.UtcNow.Year, 12, 4), new DateOnly(DateTime.UtcNow.Year, 12, 9),
                "Overlap attempt"), Actor));

        await WithHrServiceAsync(tenantId, service => service.DecideLeaveAsync(
            environment.CompanyId, first.Id, approve: true, null, Actor));
        var overlap = () => WithHrServiceAsync(tenantId, service => service.DecideLeaveAsync(
            environment.CompanyId, second.Id, approve: true, null, Actor));
        (await overlap.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already overlaps*");

        var balance = await WithHrServiceAsync(tenantId, service =>
            service.GetLeaveBalanceAsync(environment.CompanyId, employee.Id, DateTime.UtcNow.Year));
        balance.ApprovedAnnualDays.Should().Be(4m, "Dec 1-5, 2026 spans Tue-Sat: four working days");
        balance.PendingRequests.Should().Be(1, "the overlapping request stays pending");
    }

    [PostgreSqlFact]
    public async Task PayRun_PostsOneJournalExactlyOnce_WithSnapshots()
    {
        var tenantId = $"pay-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var period = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        await using (var hrContext = _fixture.CreateContext(tenantId))
        {
            var hr = BuildHrService(hrContext);
            await hr.CreateEmployeeAsync(
                new CreateEmployeeRequest(environment.CompanyId, "EMP-1", "Aisha Rahman",
                    DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)), Actor);
            await hr.CreateEmployeeAsync(
                new CreateEmployeeRequest(environment.CompanyId, "EMP-2", "Bilal Hussain",
                    DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-2)), Actor);
        }

        var structure = new SetSalaryStructureRequest(environment.CompanyId, 0, period, 2000m, 300m);
        await using (var structureContext = _fixture.CreateContext(tenantId))
        {
            var payroll = BuildPayrollService(structureContext);
            var employees = await structureContext.Employees
                .Where(employee => employee.CompanyId == environment.CompanyId)
                .OrderBy(employee => employee.Code)
                .ToListAsync();
            foreach (var employee in employees)
            {
                await payroll.SetSalaryStructureAsync(
                    structure with { EmployeeId = employee.Id }, Actor);
            }
        }

        var run = await WithPayrollServiceAsync(tenantId, service =>
            service.DraftPayRunAsync(environment.CompanyId, period, Actor));
        run.Status.Should().Be(PayRunStatus.Draft);
        run.Payslips.Should().HaveCount(2);
        run.Payslips.Should().OnlyContain(slip => slip.Gross == 2300m);

        var posted = await WithPayrollServiceAsync(tenantId, service =>
            service.PostPayRunAsync(environment.CompanyId, run.Id, Actor));
        posted.Status.Should().Be(PayRunStatus.Posted);
        posted.TotalGross.Should().Be(4600m);
        posted.TotalNet.Should().Be(4600m);

        // Posting again replays; a second run for the same month is blocked by the period index.
        var again = await WithPayrollServiceAsync(tenantId, service =>
            service.PostPayRunAsync(environment.CompanyId, run.Id, Actor));
        again.Id.Should().Be(run.Id);
        var duplicateRun = () => WithPayrollServiceAsync(tenantId, service =>
            service.DraftPayRunAsync(environment.CompanyId, period, Actor));
        (await duplicateRun.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already exists*");

        await using var verify = _fixture.CreateContext(tenantId);
        var journals = await verify.JournalEntries
            .Where(entry => entry.SourceDocumentType == "PayRun")
            .ToListAsync();
        journals.Should().HaveCount(1);
        var journal = await verify.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.SourceDocumentType == "PayRun");
        journal.TotalDebit.Should().Be(4600m).And.Be(journal.TotalCredit);
        journal.Lines.Single(line => line.Debit > 0).AccountCodeSnapshot.Should().Be("6000");
        journal.Lines.Single(line => line.Credit > 0).AccountCodeSnapshot.Should().Be("1000");

        // Payslips are snapshots: changing the structure after posting never rewrites history.
        (await verify.Payslips.SumAsync(slip => slip.Gross)).Should().Be(4600m);
    }

    [PostgreSqlFact]
    public async Task Timesheet_BillingThroughSalesInvoice_BillsApprovedTime_ReplaySafe()
    {
        var tenantId = $"bil-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        int taskId;
        var project = await WithServiceAsync(tenantId, service => service.CreateProjectAsync(
            new CreateProjectRequest(environment.CompanyId, "PRJ-BILL", "Billable dev", CustomerId: environment.CustomerId), Actor));
        var task = await WithServiceAsync(tenantId, service => service.CreateTaskAsync(
            new CreateProjectTaskRequest(environment.CompanyId, project.Id, "Feature delivery"), Actor));
        taskId = task.Id;

        var draft = await WithServiceAsync(tenantId, service => service.LogTimeAsync(
            new LogTimeRequest(environment.CompanyId, taskId, DateOnly.FromDateTime(DateTime.UtcNow), 10m,
                Billable: true, HourlyRate: 50m), "dev-1"));
        var submitted = await WithServiceAsync(tenantId, service => service.SubmitTimeAsync(
            environment.CompanyId, draft.Id, "dev-1"));
        var approved = await WithServiceAsync(tenantId, service => service.ApproveTimeAsync(
            environment.CompanyId, submitted.Id, Actor));

        var initialProfit = await WithServiceAsync(tenantId, service =>
            service.GetProfitabilityAsync(environment.CompanyId, project.Id));
        initialProfit.BillableValue.Should().Be(500m);
        initialProfit.InvoicedValue.Should().Be(0m);
        initialProfit.UninvoicedBillableValue.Should().Be(500m);

        var invoiceResult = await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                environment.CompanyId,
                environment.CustomerId,
                "INV-TIME-001",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                $"req-{Guid.NewGuid():N}",
                Lines: [],
                TimeLines: [new PostSalesInvoiceTimeLine(approved.Id)]),
            Actor));

        invoiceResult.TotalValue.Should().Be(500m);

        var billedProfit = await WithServiceAsync(tenantId, service =>
            service.GetProfitabilityAsync(environment.CompanyId, project.Id));
        billedProfit.BillableValue.Should().Be(500m);
        billedProfit.InvoicedValue.Should().Be(500m);
        billedProfit.UninvoicedBillableValue.Should().Be(0m);

        // Attempting to bill the same approved line again throws conflict
        var duplicateBill = () => WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                environment.CompanyId,
                environment.CustomerId,
                "INV-TIME-002",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                $"req-{Guid.NewGuid():N}",
                Lines: [],
                TimeLines: [new PostSalesInvoiceTimeLine(approved.Id)]),
            Actor));

        (await duplicateBill.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already billed*");
    }

    [PostgreSqlFact]
    public async Task Payroll_SettlementReportAndControlledReversal()
    {
        var tenantId = $"payrev-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var period = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        int empId;
        await using (var hrContext = _fixture.CreateContext(tenantId))
        {
            var hr = BuildHrService(hrContext);
            var emp = await hr.CreateEmployeeAsync(
                new CreateEmployeeRequest(environment.CompanyId, "EMP-REV", "Zainab Ali",
                    DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1)), Actor);
            empId = emp.Id;
        }

        await using (var structureContext = _fixture.CreateContext(tenantId))
        {
            var payroll = BuildPayrollService(structureContext);
            await payroll.SetSalaryStructureAsync(
                new SetSalaryStructureRequest(environment.CompanyId, empId, period, 3000m, 500m), Actor);
        }

        var run = await WithPayrollServiceAsync(tenantId, service =>
            service.DraftPayRunAsync(environment.CompanyId, period, Actor));
        var posted = await WithPayrollServiceAsync(tenantId, service =>
            service.PostPayRunAsync(environment.CompanyId, run.Id, Actor));

        var settlementBefore = await WithPayrollServiceAsync(tenantId, service =>
            service.GetSettlementReportAsync(environment.CompanyId, posted.Id));
        settlementBefore.Reconciled.Should().BeTrue();
        settlementBefore.Reversible.Should().BeTrue();

        var reversed = await WithPayrollServiceAsync(tenantId, service =>
            service.ReversePayRunAsync(environment.CompanyId, posted.Id, "Correction for payroll recalculation", Actor));
        reversed.Status.Should().Be(PayRunStatus.Reversed);
        reversed.ReversalReason.Should().Be("Correction for payroll recalculation");
        reversed.ReversedBy.Should().Be(Actor);

        var settlementAfter = await WithPayrollServiceAsync(tenantId, service =>
            service.GetSettlementReportAsync(environment.CompanyId, posted.Id));
        settlementAfter.Reversible.Should().BeFalse();

        // Reversing again conflicts
        var repeatReversal = () => WithPayrollServiceAsync(tenantId, service =>
            service.ReversePayRunAsync(environment.CompanyId, posted.Id, "Second reversal", Actor));
        (await repeatReversal.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*Reversed*cannot be reversed*");
    }

    [PostgreSqlFact]
    public async Task Employee_SelfServiceProfileAndSafeOffboarding()
    {
        var tenantId = $"hroff-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);

        var employee = await WithHrServiceAsync(tenantId, service => service.CreateEmployeeAsync(
            new CreateEmployeeRequest(environment.CompanyId, "EMP-SELF", "Tariq Mansoor",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), Department: "Engineering", UserId: "user-tariq"), Actor));

        var selfView = await WithHrServiceAsync(tenantId, service =>
            service.GetMyProfileAsync(environment.CompanyId, "user-tariq"));
        selfView.Should().NotBeNull();
        selfView!.EmployeeId.Should().Be(employee.Id);
        selfView.FullName.Should().Be("Tariq Mansoor");
        selfView.Department.Should().Be("Engineering");
        selfView.IsActive.Should().BeTrue();

        // Check in open attendance record
        await WithHrServiceAsync(tenantId, service => service.CheckInAsync(
            environment.CompanyId, employee.Id, "Morning shift", Actor));

        // Offboarding blocked while attendance record is open
        var blockedOffboard = () => WithHrServiceAsync(tenantId, service => service.DeactivateEmployeeAsync(
            environment.CompanyId, employee.Id, "Resigned", Actor));
        (await blockedOffboard.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*open attendance record*");

        // Check out
        await WithHrServiceAsync(tenantId, service => service.CheckOutAsync(
            environment.CompanyId, employee.Id, Actor));

        // Deactivation now succeeds
        var deactivated = await WithHrServiceAsync(tenantId, service => service.DeactivateEmployeeAsync(
            environment.CompanyId, employee.Id, "Resigned", Actor));
        deactivated.IsActive.Should().BeFalse();
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
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<SalesService>.Instance);
    }

    private async Task<T> WithSalesServiceAsync<T>(string tenantId, Func<SalesService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildSalesService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<ProjectService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildProjectService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<T> WithHrServiceAsync<T>(string tenantId, Func<HumanResourcesService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildHrService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<T> WithPayrollServiceAsync<T>(string tenantId, Func<PayrollService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildPayrollService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(string tenantId)
    {
        int companyId;
        int customerId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "HRP-MAIN", LegalName = "HR payroll test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "HRP-BR", Name = "HQ", TimeZoneId = "UTC" };
            var customer = new Customer
            {
                CustomerCode = $"CUST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                Name = "HRP customer", IsActive = true
            };
            branch.Company = company;
            customer.Company = company;
            setup.AddRange(company, branch, customer);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            customerId = customer.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1200", Name = "Accounts Receivable", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Revenue", IsGroupAccount = false, AccountClass = AccountClass.Income, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "6000", Name = "Salary Expense", AccountType = "Expense", IsGroupAccount = false, AccountClass = AccountClass.Expense, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2400", Name = "Deductions Payable", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsReceivable, ChartOfAccountId = ByCode("1200").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalesRevenue, ChartOfAccountId = ByCode("4000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalaryExpense, ChartOfAccountId = ByCode("6000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.DeductionsPayable, ChartOfAccountId = ByCode("2400").Id, IsActive = true });
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

        return new TestEnvironment { CompanyId = companyId, CustomerId = customerId };
    }

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int CustomerId { get; init; }
    }
}
