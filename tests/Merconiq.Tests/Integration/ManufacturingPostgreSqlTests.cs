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
/// Relational acceptance for manufacturing: BOM versions, work-order lifecycle, and completion
/// that conserves cost exactly — components consumed at moving average, finished goods received
/// at the identical value, paired WIP journals posted once. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class ManufacturingPostgreSqlTests
{
    private const string Actor = "mfg-tester";
    private const decimal ComponentCost = 4m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public ManufacturingPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task WorkOrderCompletion_ConservesCostExactly_AndReplays()
    {
        var tenantId = $"mfg-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);

        // 5 finished units from 2 components (2 + 3 per unit): total consumption
        // 10 × 4.00 + 15 × 4.00 = 100.00; finished unit cost 20.00.
        var bom = await WithServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 1,
            [
                new BomLineInput(environment.ComponentAId, 2),
                new BomLineInput(environment.ComponentBId, 3)
            ]), Actor));
        var workOrder = await WithServiceAsync(tenantId, service => service.CreateWorkOrderAsync(
            new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 5, environment.LocationId), Actor));
        await WithServiceAsync(tenantId, service => service.ReleaseWorkOrderAsync(
            environment.CompanyId, workOrder.Id, Actor));

        var completed = await WithServiceAsync(tenantId, service => service.CompleteWorkOrderAsync(
            environment.CompanyId, workOrder.Id, environment.MutationScope, Actor));
        var replay = await WithServiceAsync(tenantId, service => service.CompleteWorkOrderAsync(
            environment.CompanyId, workOrder.Id, environment.MutationScope, Actor));

        completed.Status.Should().Be(WorkOrderStatus.Completed);
        completed.ConsumedCost.Should().Be(100m);
        replay.Id.Should().Be(completed.Id);
        replay.ConsumedCost.Should().Be(100m);

        await using var verify = _fixture.CreateContext(tenantId);
        // Component stock: 30 − 10 and 30 − 15. Finished stock: 5 at 20.00 = 100.
        var stocks = await verify.StockInHand
            .GroupBy(stock => stock.ItemId)
            .Select(group => new { ItemId = group.Key, Quantity = group.Sum(stock => stock.Quantity) })
            .ToDictionaryAsync(row => row.ItemId);
        stocks[environment.ComponentAId].Quantity.Should().Be(20);
        stocks[environment.ComponentBId].Quantity.Should().Be(15);
        stocks[environment.FinishedItemId].Quantity.Should().Be(5);

        (await verify.StockValuationBuckets
            .Where(bucket => bucket.ItemId == environment.FinishedItemId)
            .SumAsync(bucket => bucket.Value)).Should().Be(100m);

        var journals = await verify.JournalEntries
            .Include(entry => entry.Lines)
            .Where(entry => entry.SourceDocumentType == "WorkOrder")
            .OrderBy(entry => entry.SourceDocumentId)
            .ToListAsync();
        journals.Should().HaveCount(2, "one consumption journal and one completion journal");
        journals.Sum(entry => entry.Lines.Sum(line => line.Debit)).Should()
            .Be(journals.Sum(entry => entry.Lines.Sum(line => line.Credit)));
        var wipNet = journals
            .SelectMany(entry => entry.Lines)
            .Where(line => line.AccountCodeSnapshot == "1400")
            .Sum(line => line.Debit - line.Credit);
        wipNet.Should().Be(0m, "WIP clears to zero on completion");

        // Completing again is blocked by the terminal state via replay; a cancelled order cannot complete.
        var cancel = await WithServiceAsync(tenantId, service =>
        {
            return service.CreateWorkOrderAsync(
                new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 1, environment.LocationId), Actor);
        });
        await WithServiceAsync(tenantId, service => service.ReleaseWorkOrderAsync(
            environment.CompanyId, cancel.Id, Actor));
    }

    [PostgreSqlFact]
    public async Task WorkOrder_DraftCannotComplete_AndDuplicateBomVersionConflicts()
    {
        var tenantId = $"mfg-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var bom = await WithServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 1,
                [new BomLineInput(environment.ComponentAId, 1)]), Actor));
        var workOrder = await WithServiceAsync(tenantId, service => service.CreateWorkOrderAsync(
            new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 1, environment.LocationId), Actor));

        var earlyComplete = () => WithServiceAsync(tenantId, service => service.CompleteWorkOrderAsync(
            environment.CompanyId, workOrder.Id, environment.MutationScope, Actor));
        (await earlyComplete.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*Draft*work order cannot be completed*");

        var duplicateBom = () => WithServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 1,
                [new BomLineInput(environment.ComponentBId, 1)]), Actor));
        (await duplicateBom.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already exists*");

        var selfReference = () => WithServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 2,
                [new BomLineInput(environment.FinishedItemId, 1)]), Actor));
        (await selfReference.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*own BOM*");
    }

    [PostgreSqlFact]
    public async Task MaterialProposal_ExplodesBom_DetectsShortagesAndAvailableQuantity()
    {
        var tenantId = $"mfg-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        // Stock: ComponentA = 30, ComponentB = 30
        var bom = await WithServiceAsync(tenantId, service => service.CreateBillOfMaterialsAsync(
            new CreateBomRequest(environment.CompanyId, environment.FinishedItemId, 1,
            [
                new BomLineInput(environment.ComponentAId, 2),
                new BomLineInput(environment.ComponentBId, 3)
            ]), Actor));

        // Order 5 units: requires 10 A (avail 30) and 15 B (avail 30) -> Sufficient
        var sufficientOrder = await WithServiceAsync(tenantId, service => service.CreateWorkOrderAsync(
            new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 5, environment.LocationId), Actor));
        var proposal1 = await WithServiceAsync(tenantId, service => service.GetMaterialProposalAsync(
            environment.CompanyId, sufficientOrder.Id));

        proposal1.Proceed.Should().BeTrue();
        proposal1.Lines.Should().HaveCount(2);
        proposal1.Lines.Should().OnlyContain(l => l.ShortageQuantity == 0m && l.Verdict == "Proceed");

        // Order 20 units: requires 40 A (avail 30 -> shortage 10) and 60 B (avail 30 -> shortage 30)
        var shortageOrder = await WithServiceAsync(tenantId, service => service.CreateWorkOrderAsync(
            new CreateWorkOrderRequest(environment.CompanyId, bom.Id, 20, environment.LocationId), Actor));
        var proposal2 = await WithServiceAsync(tenantId, service => service.GetMaterialProposalAsync(
            environment.CompanyId, shortageOrder.Id));

        proposal2.Proceed.Should().BeFalse();
        proposal2.Lines.Should().HaveCount(2);
        var lineA = proposal2.Lines.Single(l => l.ComponentItemId == environment.ComponentAId);
        lineA.RequiredQuantity.Should().Be(40m);
        lineA.ShortageQuantity.Should().Be(10m);
        lineA.Verdict.Should().Be("Shortage");

        var lineB = proposal2.Lines.Single(l => l.ComponentItemId == environment.ComponentBId);
        lineB.RequiredQuantity.Should().Be(60m);
        lineB.ShortageQuantity.Should().Be(30m);
        lineB.Verdict.Should().Be("Shortage");
    }

    private static ManufacturingService BuildService(InventoryDbContext context)
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

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<ManufacturingService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(string tenantId)
    {
        int companyId;
        int locationId;
        int finishedItemId;
        int componentAId;
        int componentBId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "MFG-MAIN", LegalName = "Manufacturing test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "MFG-BR", Name = "Factory", TimeZoneId = "UTC" };
            var finished = new Item
            {
                ItemCode = $"FIN-{Guid.NewGuid():N}"[..20], Description = "Finished assembly",
                Rate = 30m, IsActive = true
            };
            var componentA = new Item
            {
                ItemCode = $"CMPA-{Guid.NewGuid():N}"[..20], Description = "Component A",
                Rate = 4m, IsActive = true
            };
            var componentB = new Item
            {
                ItemCode = $"CMPB-{Guid.NewGuid():N}"[..20], Description = "Component B",
                Rate = 4m, IsActive = true
            };
            var location = new Location { Name = "Production floor" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, finished, componentA, componentB, location);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            locationId = location.Id;
            finishedItemId = finished.Id;
            componentAId = componentA.Id;
            componentBId = componentB.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1400", Name = "Work in Progress", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "3000", Name = "Opening Equity", AccountType = "Equity", IsGroupAccount = false, AccountClass = AccountClass.Equity, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.WorkInProgress, ChartOfAccountId = ByCode("1400").Id, IsActive = true });
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

        // Opening component stock: 30 of each at 4.00, journalized as opening balances.
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
                componentAId, locationId, 30, notes: "opening", unitCost: ComponentCost,
                mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
            await stockService.ReceiveStockAsync(
                componentBId, locationId, 30, notes: "opening", unitCost: ComponentCost,
                mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
        }

        await using (var journalContext = _fixture.CreateContext(tenantId))
        {
            var unitOfWork = new UnitOfWork(journalContext);
            var fiscalCalendar = new FiscalCalendarService(journalContext, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
            var posting = new JournalPostingService(journalContext, unitOfWork, fiscalCalendar,
                new DocumentNumberService(journalContext, unitOfWork), NullLogger<JournalPostingService>.Instance);
            var inventoryAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "1300");
            var equityAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "3000");
            await posting.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    "USD",
                    "Opening component stock",
                    "OpeningBalance",
                    $"inventory-{companyId}",
                    "Post",
                    1,
                    [
                        new JournalLineInput(inventoryAccount.Id, "Opening components", 240m, 0m),
                        new JournalLineInput(equityAccount.Id, "Opening equity", 0m, 240m)
                    ]),
                Actor);
        }

        return new TestEnvironment
        {
            CompanyId = companyId,
            LocationId = locationId,
            FinishedItemId = finishedItemId,
            ComponentAId = componentAId,
            ComponentBId = componentBId,
            MutationScope = new StockMutationScope(companyId, () => Task.FromResult(true))
        };
    }

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int LocationId { get; init; }
        public int FinishedItemId { get; init; }
        public int ComponentAId { get; init; }
        public int ComponentBId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }
}
