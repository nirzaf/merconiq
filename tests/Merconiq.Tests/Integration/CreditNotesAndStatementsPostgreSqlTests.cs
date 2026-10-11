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
/// Relational acceptance for bounded credit notes (supplier and sales) and ledger-backed
/// financial statements. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class CreditNotesAndStatementsPostgreSqlTests
{
    private const string Actor = "credit-note-tester";
    private const decimal CostPerUnit = 10m;
    private const decimal SalePrice = 15m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public CreditNotesAndStatementsPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task SupplierCreditNote_RestocksNothing_CreditsAp_AndIsBounded()
    {
        var tenantId = $"cn-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 50, accepted: 40);

        var note = await WithServiceAsync(tenantId, service => service.PostPurchaseCreditNoteAsync(
            new PostPurchaseCreditNoteRequest(
                environment.CompanyId, environment.SupplierId, "SUP-CN-1", "Damaged on arrival",
                "pcn-1",
                [new PostPurchaseCreditNoteLine(environment.GoodsReceiptLineId, 10)]),
            environment.MutationScope,
            Actor));

        note.TotalValue.Should().Be(100m, "10 units at the receipt's 10.00 unit cost");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(50,
            "20 opening plus 40 received less 10 returned");
        (await verify.StockValuationBuckets.SumAsync(bucket => bucket.Value)).Should().Be(500m);

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        var payables = trialBalance.Rows.Single(row => row.AccountCode == "2000");
        (payables.Debit).Should().Be(100m, "AP debited by the supplier credit");
        var inventory = trialBalance.Rows.Single(row => row.AccountCode == "1300");
        (inventory.Debit - inventory.Credit).Should().Be(500m);
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        // Crediting 40 more exceeds the returnable balance of 30.
        var over = () => WithServiceAsync(tenantId, service => service.PostPurchaseCreditNoteAsync(
            new PostPurchaseCreditNoteRequest(
                environment.CompanyId, environment.SupplierId, "SUP-CN-2", "Second attempt",
                "pcn-2",
                [new PostPurchaseCreditNoteLine(environment.GoodsReceiptLineId, 40)]),
            environment.MutationScope,
            Actor));
        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*returnable balance*");
    }

    [PostgreSqlFact]
    public async Task SupplierCreditNote_RejectsInvoiceMatchedReceiptLine()
    {
        var tenantId = $"cn-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 50, accepted: 40);
        await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            new PostPurchaseInvoiceRequest(
                environment.CompanyId, environment.SupplierId, "SUP-INV-1",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                "pinv-1",
                [new PostPurchaseInvoiceLine(environment.GoodsReceiptLineId)]),
            Actor));

        var credit = () => WithServiceAsync(tenantId, service => service.PostPurchaseCreditNoteAsync(
            new PostPurchaseCreditNoteRequest(
                environment.CompanyId, environment.SupplierId, "SUP-CN-1", "Late return",
                "pcn-1",
                [new PostPurchaseCreditNoteLine(environment.GoodsReceiptLineId, 5)]),
            environment.MutationScope,
            Actor));

        (await credit.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already matched*by a posted invoice*");
    }

    [PostgreSqlFact]
    public async Task SalesCreditNote_RestocksStock_ReversesRevenue_AndIsBounded()
    {
        var tenantId = $"cn-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithDeliveryAsync(tenantId, orderedQuantity: 20, delivered: 10);

        var note = await WithServiceAsync(tenantId, service => service.PostSalesCreditNoteAsync(
            new PostSalesCreditNoteRequest(
                environment.CompanyId, environment.CustomerId, "CUST-CN-1", "Wrong size",
                "scn-1",
                [new PostSalesCreditNoteLine(environment.DeliveryLineId, 4)]),
            environment.MutationScope,
            Actor));

        note.TotalValue.Should().Be(60m, "4 units at the sold 15.00 price");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(14, "10 delivered, 4 returned");
        (await verify.StockValuationBuckets.SumAsync(bucket => bucket.Value)).Should().Be(140m);

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        var revenue = trialBalance.Rows.Single(row => row.AccountCode == "4000");
        (revenue.Credit - revenue.Debit).Should().Be(90m, "150 invoiced less 60 credited");
        var receivable = trialBalance.Rows.Single(row => row.AccountCode == "1100");
        (receivable.Debit - receivable.Credit).Should().Be(90m);
        var inventory = trialBalance.Rows.Single(row => row.AccountCode == "1300");
        (inventory.Debit - inventory.Credit).Should().Be(140m);
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        // Crediting 10 more exceeds the returnable balance of 6.
        var over = () => WithServiceAsync(tenantId, service => service.PostSalesCreditNoteAsync(
            new PostSalesCreditNoteRequest(
                environment.CompanyId, environment.CustomerId, "CUST-CN-2", "Second attempt",
                "scn-2",
                [new PostSalesCreditNoteLine(environment.DeliveryLineId, 10)]),
            environment.MutationScope,
            Actor));
        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*returnable balance*");
    }

    [PostgreSqlFact]
    public async Task FinancialStatements_MatchIndependentFixture_AndBalance()
    {
        var tenantId = $"cn-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithDeliveryAsync(tenantId, orderedQuantity: 20, delivered: 10);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var profitAndLoss = await WithStatementServiceAsync(tenantId, service =>
            service.GetProfitAndLossAsync(environment.CompanyId, today.AddDays(-7), today));
        profitAndLoss.TotalIncome.Should().Be(150m, "one sales invoice for 10 × 15");
        profitAndLoss.TotalExpenses.Should().Be(100m, "COGS for 10 units at 10.00");
        profitAndLoss.NetProfit.Should().Be(50m);

        var balanceSheet = await WithStatementServiceAsync(tenantId, service =>
            service.GetBalanceSheetAsync(environment.CompanyId, today));
        balanceSheet.Balances.Should().BeTrue();
        balanceSheet.NetProfit.Should().Be(50m, "revenue 150 less COGS 100");
        var inventory = balanceSheet.Assets.Single(line => line.AccountCode == "1300");
        inventory.Amount.Should().Be(100m, "10 units of stock remain");
        var receivable = balanceSheet.Assets.Single(line => line.AccountCode == "1100");
        receivable.Amount.Should().Be(150m, "invoiced, not yet paid");
        var equity = balanceSheet.Equity.Single(line => line.AccountCode == "3000");
        equity.Amount.Should().Be(200m, "opening inventory equity");
        balanceSheet.TotalAssets.Should().Be(250m);
        balanceSheet.TotalLiabilities.Should().Be(0m);
        (balanceSheet.TotalEquity + balanceSheet.NetProfit).Should().Be(250m,
            "assets = liabilities + equity + retained profit");
    }

    private static CreditNoteService BuildCreditNoteService(InventoryDbContext context)
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
        return new CreditNoteService(
            context, unitOfWork, stockService,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<CreditNoteService>.Instance);
    }

    private static PurchaseInvoiceService BuildInvoiceService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PurchaseInvoiceService(
            context, unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PurchaseInvoiceService>.Instance);
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

    private static FinanceStatementService BuildStatementService(InventoryDbContext context) => new(context);

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<CreditNoteService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildCreditNoteService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
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

    private async Task<T> WithStatementServiceAsync<T>(string tenantId, Func<FinanceStatementService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildStatementService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TrialBalanceResult> TrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var posting = new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
        return await posting.GetTrialBalanceAsync(companyId, null, null);
    }

    private async Task<TestEnvironment> CreateEnvironmentWithReceiptAsync(
        string tenantId, int orderedQuantity, int accepted)
    {
        var environment = await CreateEnvironmentAsync(tenantId);
        int purchaseOrderId;
        int purchaseOrderLineId;
        int goodsReceiptLineId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var supplier = new Supplier { Name = "Credit-note supplier" };
            setup.Suppliers.Add(supplier);
            await setup.SaveChangesAsAsync(Actor);

            var order = new PurchaseOrder
            {
                PONumber = $"PO-{Guid.NewGuid():N}"[..20],
                OrderDate = DateTime.UtcNow,
                SupplierId = supplier.Id,
                Status = PurchaseOrderStatus.Approved,
                CurrencyScale = 2,
                CalculationVersion = 1,
                CommercialVersion = 1,
                ApprovedCommercialVersion = 1,
                ApprovedCommercialSnapshotJson = "{}",
                TotalAmount = orderedQuantity * CostPerUnit,
                NetAmount = orderedQuantity * CostPerUnit,
                OrderDetails =
                [
                    new OrderDetail
                    {
                        ItemId = environment.ItemId,
                        Quantity = orderedQuantity,
                        UnitPrice = CostPerUnit,
                        CurrencyScale = 2,
                        CalculationVersion = 1,
                        NetAmount = orderedQuantity * CostPerUnit,
                        GrossAmount = orderedQuantity * CostPerUnit
                    }
                ]
            };
            setup.PurchaseOrders.Add(order);
            var detail = order.OrderDetails.Single();
            var document = DocumentIdentity.Create(
                order.DocumentId, tenantId, environment.CompanyId, "PurchaseOrder", order.PONumber,
                DateTime.UtcNow.Year, DocumentLifecycleStatus.Active, "CreditNoteTest");
            var lineIdentity = DocumentLineIdentity.Create(
                detail.DocumentLineId, document.Id, tenantId, environment.CompanyId, "PurchaseOrderLine");
            setup.DocumentIdentities.Add(document);
            setup.DocumentLineIdentities.Add(lineIdentity);
            await setup.SaveChangesAsAsync(Actor);
            purchaseOrderId = order.Id;
            purchaseOrderLineId = detail.Id;
        }

        await using (var receiptContext = _fixture.CreateContext(tenantId))
        {
            var unitOfWork = new UnitOfWork(receiptContext);
            var webhookDispatcher = new Mock<IWebhookDispatcher>();
            webhookDispatcher
                .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var stockService = new StockService(
                new Repository<StockInHand>(receiptContext),
                new Repository<StockTransaction>(receiptContext),
                new Repository<Item>(receiptContext),
                new Repository<Location>(receiptContext),
                new Repository<Branch>(receiptContext),
                unitOfWork,
                webhookDispatcher.Object,
                new TestTenantContext(receiptContext.CurrentTenantId),
                NullLogger<StockService>.Instance,
                new Repository<StockValuationBucket>(receiptContext),
                new Repository<StockValuationEntry>(receiptContext));
            var fiscalCalendar = new FiscalCalendarService(receiptContext, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
            var documentNumbers = new DocumentNumberService(receiptContext, unitOfWork);
            var goodsReceiptService = new GoodsReceiptService(
                receiptContext, unitOfWork, stockService,
                new JournalPostingService(
                    receiptContext, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
                new AccountPostingPolicyService(receiptContext),
                fiscalCalendar,
                documentNumbers,
                NullLogger<GoodsReceiptService>.Instance);
            await goodsReceiptService.PostGoodsReceiptAsync(
                new PostGoodsReceiptRequest(
                    environment.CompanyId, purchaseOrderId, environment.LocationId, "cn-receipt",
                    [new PostGoodsReceiptLine(purchaseOrderLineId, accepted)]),
                environment.MutationScope,
                Actor);
            goodsReceiptLineId = (await receiptContext.GoodsReceiptLines.SingleAsync()).Id;
        }

        return environment with
        {
            SupplierId = (await _fixture.CreateContext(tenantId).Suppliers.SingleAsync()).Id,
            PurchaseOrderId = purchaseOrderId,
            GoodsReceiptLineId = goodsReceiptLineId
        };
    }

    private async Task<TestEnvironment> CreateEnvironmentWithDeliveryAsync(
        string tenantId, int orderedQuantity, int delivered)
    {
        var environment = await CreateEnvironmentAsync(tenantId);
        int salesOrderId;
        int salesOrderLineId;
        int deliveryLineId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var customer = new Customer
            {
                CustomerCode = $"CUST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                Name = "Credit-note customer", IsActive = true, CompanyId = environment.CompanyId
            };
            setup.Customers.Add(customer);
            await setup.SaveChangesAsAsync(Actor);
        }

        var salesContext = _fixture.CreateContext(tenantId);
        try
        {
            var salesService = BuildSalesService(salesContext);
            var order = await salesService.PostSalesOrderAsync(
                new PostSalesOrderRequest(
                    environment.CompanyId,
                    (await salesContext.Customers.SingleAsync()).Id,
                    "cn-order",
                    [new PostSalesOrderLine(environment.ItemId, orderedQuantity, SalePrice)]),
                Actor);
            salesOrderId = order.SalesOrderId;
            salesOrderLineId = order.FirstLineId;

            var delivery = await salesService.PostDeliveryAsync(
                new PostDeliveryRequest(
                    environment.CompanyId, salesOrderId, environment.LocationId, "cn-delivery",
                    [new PostDeliveryLine(salesOrderLineId, delivered)]),
                environment.MutationScope,
                Actor);
            deliveryLineId = delivery.FirstLineId;

            // Invoice the delivery so the fixture carries recognized revenue for the P&L.
            await salesService.PostSalesInvoiceAsync(
                new PostSalesInvoiceRequest(
                    environment.CompanyId,
                    (await salesContext.Customers.SingleAsync()).Id,
                    "CUST-INV-1",
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                    "cn-invoice",
                    [new PostSalesInvoiceLine(delivery.FirstLineId)]),
                Actor);
        }
        finally
        {
            await salesContext.DisposeAsync();
        }

        return environment with
        {
            CustomerId = (await _fixture.CreateContext(tenantId).Customers.SingleAsync()).Id,
            SalesOrderId = salesOrderId,
            DeliveryLineId = deliveryLineId
        };
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(string tenantId)
    {
        int companyId;
        int locationId;
        int itemId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "CN-MAIN", LegalName = "Credit-note test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "CN-BR", Name = "Main branch", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"CN-{Guid.NewGuid():N}"[..20], Description = "Credit-note item",
                Rate = SalePrice, IsActive = true
            };
            var location = new Location { Name = "Main warehouse" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, item, location);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            locationId = location.Id;
            itemId = item.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1100", Name = "Accounts Receivable", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "Goods Received Not Invoiced", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "3000", Name = "Opening Equity", AccountType = "Equity", IsGroupAccount = false, AccountClass = AccountClass.Equity, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", IsGroupAccount = false, AccountClass = AccountClass.Income, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5000", Name = "Cost of Goods Sold", AccountType = "Expense", IsGroupAccount = false, AccountClass = AccountClass.Expense, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsReceivable, ChartOfAccountId = ByCode("1100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsPayable, ChartOfAccountId = ByCode("2000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced, ChartOfAccountId = ByCode("2100").Id, IsActive = true },
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

        // Opening stock of 20 units at cost 10 with its opening-balance journal.
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
                itemId, locationId, 20, notes: "opening stock",
                unitCost: CostPerUnit,
                mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
        }

        await using (var journalContext = _fixture.CreateContext(tenantId))
        {
            var unitOfWork = new UnitOfWork(journalContext);
            var fiscalCalendar = new FiscalCalendarService(journalContext, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
            var posting = new JournalPostingService(
                journalContext, unitOfWork, fiscalCalendar, new DocumentNumberService(journalContext, unitOfWork),
                NullLogger<JournalPostingService>.Instance);
            var inventoryAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "1300");
            var equityAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "3000");
            await posting.PostAsync(
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
                        new JournalLineInput(inventoryAccount.Id, "Opening stock", 200m, 0m),
                        new JournalLineInput(equityAccount.Id, "Opening equity", 0m, 200m)
                    ]),
                Actor);
        }

        return new TestEnvironment
        {
            CompanyId = companyId,
            LocationId = locationId,
            ItemId = itemId,
            MutationScope = new StockMutationScope(companyId, () => Task.FromResult(true))
        };
    }

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public StockMutationScope MutationScope { get; init; }

        public int SupplierId { get; init; }
        public int PurchaseOrderId { get; init; }
        public int GoodsReceiptLineId { get; init; }
        public int CustomerId { get; init; }
        public int SalesOrderId { get; init; }
        public int DeliveryLineId { get; init; }
    }
}
