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
/// Relational acceptance for the order-to-cash slice, following the review's golden fixture
/// shape: deliver stock at moving-average cost (COGS = exact issued value), invoice revenue once,
/// and settle the receivable. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class SalesOrderToCashPostgreSqlTests
{
    private const string Actor = "o2c-tester";
    private const decimal CostPerUnit = 10m;
    private const decimal SalePrice = 15m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public SalesOrderToCashPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task OrderToCash_GoldenPath_PostsEachEffectExactlyOnce()
    {
        var tenantId = $"o2c-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 60);

        // Order: commitment only, no ledger effect.
        var before = await TrialBalanceAsync(tenantId, environment.CompanyId);
        var order = await WithSalesServiceAsync(tenantId, service => service.PostSalesOrderAsync(
            Order(environment, quantity: 10, price: SalePrice), Actor));
        var afterOrder = await TrialBalanceAsync(tenantId, environment.CompanyId);
        afterOrder.TotalDebit.Should().Be(before.TotalDebit, "order approval never posts");

        // Delivery: issues 10 units at cost 10 → COGS 100 / Inventory 100; stock 60 → 50.
        var delivery = await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 10),
            environment.MutationScope, Actor));
        delivery.Replayed.Should().BeFalse();
        delivery.CostOfGoodsSold.Should().Be(100m);
        delivery.JournalNumber.Should().StartWith("JV");

        // Invoice: revenue 10 × 15 = 150 → AR 150 / Sales 150.
        var invoice = await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            SalesInvoice(environment, delivery.FirstLineId, environment.CustomerId), Actor));
        invoice.TotalValue.Should().Be(150m);
        invoice.OpenValue.Should().Be(150m);

        // Customer receipt: 100 → Bank 100 / AR 100; AR open 50.
        var receipt = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            Receipt(environment, invoice.SalesInvoiceId, amount: 100m), Actor));
        receipt.Replayed.Should().BeFalse();

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(50);
        (await verify.StockValuationBuckets.SumAsync(bucket => bucket.Value)).Should().Be(500m);

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        trialBalance.Rows.Single(row => row.AccountCode == "5000").Debit.Should().Be(100m, "COGS");
        var inventory = trialBalance.Rows.Single(row => row.AccountCode == "1300");
        (inventory.Debit - inventory.Credit).Should().Be(500m, "inventory net of the issued cost");
        var receivable = trialBalance.Rows.Single(row => row.AccountCode == "1100");
        (receivable.Debit - receivable.Credit).Should().Be(50m, "AR 150 less receipt 100");
        trialBalance.Rows.Single(row => row.AccountCode == "4000").Credit.Should().Be(150m, "revenue");
        trialBalance.Rows.Single(row => row.AccountCode == "1000").Debit.Should().Be(100m, "bank receipt");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        var progress = await WithSalesServiceAsync(
            tenantId, service => service.GetSalesOrderProgressAsync(environment.CompanyId, order.SalesOrderId));
        progress!.DeliveredQuantity.Should().Be(10);
        progress.OutstandingQuantity.Should().Be(0);

        var openReceivable = invoice.TotalValue - 100m;
        openReceivable.Should().Be(50m);
    }

    [PostgreSqlFact]
    public async Task Delivery_OverDelivery_IsRejected()
    {
        var tenantId = $"o2c-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 60);
        var order = await WithSalesServiceAsync(tenantId, service => service.PostSalesOrderAsync(
            Order(environment, quantity: 10, price: SalePrice), Actor));
        await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 10, key: "dn-1"),
            environment.MutationScope, Actor));

        var over = async () => await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 1, key: "dn-2"),
            environment.MutationScope, Actor));

        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*exceed the ordered*");
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(50);
    }

    [PostgreSqlFact]
    public async Task Delivery_CommittedRequestKey_Replays_WithoutSecondStockIssue()
    {
        var tenantId = $"o2c-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 60);
        var order = await WithSalesServiceAsync(tenantId, service => service.PostSalesOrderAsync(
            Order(environment, quantity: 10, price: SalePrice), Actor));
        var request = Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 10);

        var first = await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            request, environment.MutationScope, Actor));
        var second = await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            request, environment.MutationScope, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.DeliveryNoteId.Should().Be(first.DeliveryNoteId);
        second.CostOfGoodsSold.Should().Be(first.CostOfGoodsSold);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.DeliveryNotes.CountAsync()).Should().Be(1);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(50);
        (await verify.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "DeliveryNote"))
            .Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task SalesInvoice_DoubleMatchingDeliveryLine_Conflicts()
    {
        var tenantId = $"o2c-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 60);
        var order = await WithSalesServiceAsync(tenantId, service => service.PostSalesOrderAsync(
            Order(environment, quantity: 10, price: SalePrice), Actor));
        var delivery = await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 10),
            environment.MutationScope, Actor));
        await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            SalesInvoice(environment, delivery.FirstLineId, environment.CustomerId), Actor));

        var second = async () => await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            SalesInvoice(environment, delivery.FirstLineId, environment.CustomerId, key: "sinv-2"), Actor));

        (await second.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already matched by a posted invoice*");
    }

    [PostgreSqlFact]
    public async Task CustomerReceipt_OverAllocation_IsRejected()
    {
        var tenantId = $"o2c-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 60);
        var order = await WithSalesServiceAsync(tenantId, service => service.PostSalesOrderAsync(
            Order(environment, quantity: 10, price: SalePrice), Actor));
        var delivery = await WithSalesServiceAsync(tenantId, service => service.PostDeliveryAsync(
            Delivery(environment, order.SalesOrderId, order.FirstLineId, quantity: 10),
            environment.MutationScope, Actor));
        var invoice = await WithSalesServiceAsync(tenantId, service => service.PostSalesInvoiceAsync(
            SalesInvoice(environment, delivery.FirstLineId, environment.CustomerId), Actor));

        var over = async () => await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            Receipt(environment, invoice.SalesInvoiceId, amount: 200m), Actor));

        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*exceeds its open balance*");
    }

    private static PostSalesOrderRequest Order(
        TestEnvironment environment, int quantity, decimal price, string key = "so-1") => new(
        environment.CompanyId,
        environment.CustomerId,
        key,
        [new PostSalesOrderLine(environment.ItemId, quantity, price)]);

    private static PostDeliveryRequest Delivery(
        TestEnvironment environment, int salesOrderId, int salesOrderLineId, int quantity,
        string key = "dn-1") => new(
        environment.CompanyId,
        salesOrderId,
        environment.LocationId,
        key,
        [new PostDeliveryLine(salesOrderLineId, quantity)]);

    private static PostSalesInvoiceRequest SalesInvoice(
        TestEnvironment environment, int deliveryLineId, int customerId, string key = "sinv-1") => new(
        environment.CompanyId,
        customerId,
        $"CUST-INV-{key}",
        DateOnly.FromDateTime(DateTime.UtcNow),
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
        key,
        [new PostSalesInvoiceLine(deliveryLineId)]);

    private static PostPaymentRequest Receipt(
        TestEnvironment environment, int salesInvoiceId, decimal amount, string key = "rcpt-1") => new(
        environment.CompanyId,
        PaymentEntryType.Receive,
        PaymentMethod.BankTransfer,
        key,
        amount,
        [new PostPaymentAllocation(salesInvoiceId, amount)],
        CustomerId: environment.CustomerId);

    private static JournalPostingService BuildPostingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
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

    private static PaymentService BuildPaymentService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PaymentService(
            context, unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PaymentService>.Instance);
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

    private async Task<T> WithPaymentServiceAsync<T>(string tenantId, Func<PaymentService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildPaymentService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TrialBalanceResult> TrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await BuildPostingService(context).GetTrialBalanceAsync(companyId, null, null);
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(string tenantId, int openingStock)
    {
        int companyId;
        int customerId;
        int locationId;
        int itemId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "O2C-MAIN", LegalName = "O2C test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "O2C-BR", Name = "Main branch", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"O2C-{Guid.NewGuid():N}"[..20], Description = "O2C item",
                Rate = SalePrice, IsActive = true
            };
            var location = new Location { Name = "Sales warehouse" };
            var customer = new Customer
            {
                CustomerCode = $"CUST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                Name = "O2C customer", IsActive = true
            };
            branch.Company = company;
            location.Branch = branch;
            customer.Company = company;
            setup.AddRange(company, branch, item, location, customer);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            customerId = customer.Id;
            locationId = location.Id;
            itemId = item.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1100", Name = "Accounts Receivable", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "3000", Name = "Opening Equity", AccountType = "Equity", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5000", Name = "Cost of Goods Sold", AccountType = "Expense", IsGroupAccount = false, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsReceivable, ChartOfAccountId = ByCode("1100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalesRevenue, ChartOfAccountId = ByCode("4000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CostOfGoodsSold, ChartOfAccountId = ByCode("5000").Id, IsActive = true });
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

        // Opening valued stock through the real stock service: 60 units at cost 10.
        var stockContext = _fixture.CreateContext(tenantId);
        int openingStockValue = 0;
        try
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
                itemId, locationId, openingStock, notes: "opening stock",
                unitCost: CostPerUnit,
                mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
            openingStockValue = openingStock * 10;
        }
        finally
        {
            await stockContext.DisposeAsync();
        }

        // The stock service alone does not post GL; the opening balance journals through the
        // kernel (Dr Inventory / Cr OpeningEquity) so the trial balance reconciles with stock.
        await using (var journalContext = _fixture.CreateContext(tenantId))
        {
            var journalPosting = BuildPostingService(journalContext);
            var inventoryAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "1300");
            var equityAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "3000");
            await journalPosting.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    "USD",
                    "Opening inventory balance",
                    "OpeningBalance",
                    $"inventory-{companyId}",
                    "Post",
                    1,
                    [
                        new JournalLineInput(inventoryAccount.Id, "Opening stock", openingStockValue, 0m),
                        new JournalLineInput(equityAccount.Id, "Opening equity", 0m, openingStockValue)
                    ]),
                Actor);
        }

        // Post a delivery once to capture its line for invoice tests; the golden-path test
        // creates its own delivery, so this environment only prepares the receipt side.
        return new TestEnvironment
        {
            CompanyId = companyId,
            CustomerId = customerId,
            LocationId = locationId,
            ItemId = itemId,
            MutationScope = new StockMutationScope(companyId, () => Task.FromResult(true))
        };
    }

    private sealed class TestEnvironment
    {
        public int CompanyId { get; init; }
        public int CustomerId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }
}
