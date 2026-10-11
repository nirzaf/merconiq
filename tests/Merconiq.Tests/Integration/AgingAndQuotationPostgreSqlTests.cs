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
/// Relational acceptance for AR/AP aging buckets and the quotation-to-order stage. Requires
/// RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class AgingAndQuotationPostgreSqlTests
{
    private const string Actor = "aging-quote-tester";
    private const decimal CostPerUnit = 10m;
    private const decimal SalePrice = 15m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public AgingAndQuotationPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task ReceivablesAging_BucketsOpenBalanceByDueDate()
    {
        var tenantId = $"agq-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 20);
        var posting = BuildSalesService(_fixture.CreateContext(tenantId));
        try
        {
            var order = await posting.PostSalesOrderAsync(new PostSalesOrderRequest(
                environment.CompanyId, environment.CustomerId, "ag-order",
                [new PostSalesOrderLine(environment.ItemId, 10, SalePrice)]), Actor);
            var delivery = await posting.PostDeliveryAsync(new PostDeliveryRequest(
                environment.CompanyId, order.SalesOrderId, environment.LocationId, "ag-delivery",
                [new PostDeliveryLine(order.FirstLineId, 10)]), environment.MutationScope, Actor);
            var invoice = await posting.PostSalesInvoiceAsync(new PostSalesInvoiceRequest(
                environment.CompanyId, environment.CustomerId, "AG-INV-1",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "ag-invoice",
                [new PostSalesInvoiceLine(delivery.FirstLineId)]), Actor);
            await BuildPaymentService(_fixture.CreateContext(tenantId)).PostPaymentAsync(
                new PostPaymentRequest(environment.CompanyId, PaymentEntryType.Receive, PaymentMethod.BankTransfer,
                    "ag-receipt", 50m, [new PostPaymentAllocation(invoice.SalesInvoiceId, 50m)],
                    CustomerId: environment.CustomerId), Actor);
        }
        finally
        {
            // posting context disposed inside helper calls
        }

        var aging = await WithSalesServiceAsync(tenantId, service =>
            service.GetReceivablesAgingAsync(environment.CompanyId));
        aging.TotalOpen.Should().Be(100m, "invoiced 150 less receipt 50");
        aging.Rows.Should().HaveCount(1);
        aging.Rows.Single().NotDue.Should().Be(100m, "due in 14 days");
        aging.Rows.Single().Days1To30.Should().Be(0m);
    }

    [PostgreSqlFact]
    public async Task PayablesAging_BucketsOpenBalanceByDueDate()
    {
        var tenantId = $"agq-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 20);
        int supplierId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var supplier = new Supplier { Name = "Aging supplier" };
            setup.Suppliers.Add(supplier);
            await setup.SaveChangesAsAsync(Actor);
            supplierId = supplier.Id;
        }

        await using (var receiptContext = _fixture.CreateContext(tenantId))
        {
            var receiptService = BuildGoodsReceiptService(receiptContext);
            var order = await SeedPurchaseOrderAsync(receiptContext, tenantId, environment, supplierId, 30);
            await receiptService.PostGoodsReceiptAsync(
                new PostGoodsReceiptRequest(environment.CompanyId, order.id, environment.LocationId, "ag-receipt",
                    [new PostGoodsReceiptLine(order.lineId, 30)]),
                environment.MutationScope, Actor);
            var receiptLineId = (await receiptContext.GoodsReceiptLines.SingleAsync()).Id;
            await receiptContext.DisposeAsync();

            var invoiceContext = _fixture.CreateContext(tenantId);
            var invoiceService = BuildInvoiceService(invoiceContext);
            await invoiceService.PostPurchaseInvoiceAsync(new PostPurchaseInvoiceRequest(
                environment.CompanyId, supplierId, "AG-SUP-INV",
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-46), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-45),
                "ag-pinvoice", [new PostPurchaseInvoiceLine(receiptLineId)]), Actor);
            await invoiceContext.DisposeAsync();
        }

        var aging = await WithInvoiceServiceAsync(tenantId, service =>
            service.GetPayablesAgingAsync(environment.CompanyId));
        aging.TotalOpen.Should().Be(300m);
        aging.Rows.Single().Days31To60.Should().Be(300m, "due 45 days ago");
    }

    [PostgreSqlFact]
    public async Task Quotation_ConvertsOnce_IntoBoundedOrder_AndReplays()
    {
        var tenantId = $"agq-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 20);
        var quotation = await WithSalesServiceAsync(tenantId, service => service.PostQuotationAsync(
            new PostQuotationRequest(environment.CompanyId, environment.CustomerId, "qt-1",
                [new PostQuotationLine(environment.ItemId, 10, SalePrice)],
                Notes: "Fleet offer"), Actor));

        quotation.Replayed.Should().BeFalse();
        quotation.TotalValue.Should().Be(150m);

        var first = await WithSalesServiceAsync(tenantId, service => service.ConvertQuotationAsync(
            new ConvertQuotationRequest(environment.CompanyId, quotation.QuotationId), Actor));
        var second = await WithSalesServiceAsync(tenantId, service => service.ConvertQuotationAsync(
            new ConvertQuotationRequest(environment.CompanyId, quotation.QuotationId), Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.SalesOrderId.Should().Be(first.SalesOrderId);

        await using var verify = _fixture.CreateContext(tenantId);
        var stored = await verify.Quotations.Include(q => q.Lines).SingleAsync();
        stored.Status.Should().Be(QuotationStatus.Accepted);
        stored.SalesOrderId.Should().Be(first.SalesOrderId);
        stored.Lines.Single().Quantity.Should().Be(10);
        (await verify.SalesOrders.CountAsync()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task Quotation_Expired_CannotConvert()
    {
        var tenantId = $"agq-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, openingStock: 20);
        var quotation = await WithSalesServiceAsync(tenantId, service => service.PostQuotationAsync(
            new PostQuotationRequest(environment.CompanyId, environment.CustomerId, "qt-exp",
                [new PostQuotationLine(environment.ItemId, 5, SalePrice)],
                QuotationDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
                ValidUntil: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)), Actor));

        var convert = () => WithSalesServiceAsync(tenantId, service => service.ConvertQuotationAsync(
            new ConvertQuotationRequest(environment.CompanyId, quotation.QuotationId), Actor));

        (await convert.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*expired*");
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.Quotations.SingleAsync()).Status.Should().Be(QuotationStatus.Expired);
    }

    private async Task<T> WithInvoiceServiceAsync<T>(string tenantId, Func<PurchaseInvoiceService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildInvoiceService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private static PurchaseInvoiceService BuildInvoiceService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PurchaseInvoiceService(
            context, unitOfWork,
            new JournalPostingService(context, unitOfWork, fiscalCalendar,
                new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PurchaseInvoiceService>.Instance);
    }

    private static GoodsReceiptService BuildGoodsReceiptService(InventoryDbContext context)
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
        return new GoodsReceiptService(
            context, unitOfWork, stockService,
            new JournalPostingService(context, unitOfWork, fiscalCalendar, documentNumbers,
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<GoodsReceiptService>.Instance);
    }

    private static async Task<(int id, int lineId)> SeedPurchaseOrderAsync(
        InventoryDbContext setup,
        string tenantId,
        object environment,
        int supplierId,
        int orderedQuantity)
    {
        var companyId = (int)environment.GetType().GetProperty("CompanyId")!.GetValue(environment)!;
        var itemId = (int)environment.GetType().GetProperty("ItemId")!.GetValue(environment)!;
        var order = new PurchaseOrder
        {
            PONumber = $"PO-{Guid.NewGuid():N}"[..20],
            OrderDate = DateTime.UtcNow,
            SupplierId = supplierId,
            Status = PurchaseOrderStatus.Approved,
            CurrencyScale = 2,
            CalculationVersion = 1,
            CommercialVersion = 1,
            ApprovedCommercialVersion = 1,
            ApprovedCommercialSnapshotJson = "{}",
            TotalAmount = orderedQuantity * 10m,
            NetAmount = orderedQuantity * 10m,
            OrderDetails =
            [
                new OrderDetail
                {
                    ItemId = itemId,
                    Quantity = orderedQuantity,
                    UnitPrice = 10m,
                    CurrencyScale = 2,
                    CalculationVersion = 1,
                    NetAmount = orderedQuantity * 10m,
                    GrossAmount = orderedQuantity * 10m
                }
            ]
        };
        setup.PurchaseOrders.Add(order);
        var detail = order.OrderDetails.Single();
        var document = DocumentIdentity.Create(
            order.DocumentId, tenantId, companyId, "PurchaseOrder", order.PONumber,
            DateTime.UtcNow.Year, DocumentLifecycleStatus.Active, "AgingTest");
        var lineIdentity = DocumentLineIdentity.Create(
            detail.DocumentLineId, document.Id, tenantId, companyId, "PurchaseOrderLine");
        setup.DocumentIdentities.Add(document);
        setup.DocumentLineIdentities.Add(lineIdentity);
        await setup.SaveChangesAsAsync("aging-quote-tester");
        return (order.Id, detail.Id);
    }

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
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "Goods Received Not Invoiced", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
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
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CostOfGoodsSold, ChartOfAccountId = ByCode("5000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsPayable, ChartOfAccountId = ByCode("2000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced, ChartOfAccountId = ByCode("2100").Id, IsActive = true });
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

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int CustomerId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }
}
