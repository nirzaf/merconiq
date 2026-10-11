using FluentAssertions;
using Merconiq.Core.Entities;
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
/// Proves [MER-M10-S01] (#304) and canonical golden fixture in docs/ACCEPTANCE_SCENARIOS.md:
/// Integrated Core ERP accounting scenario from procure-to-pay through order-to-cash.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class CoreErpScenarioPostgreSqlTests
{
    private const string Actor = "core-erp-scenario-runner";
    private readonly PostgreSqlIntegrationFixture _fixture;

    public CoreErpScenarioPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task CanonicalScenario_P2P_And_O2C_CompletesWithExactLedgerAndIsolation()
    {
        var tenantId = $"core-erp-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId);

        // Step 2: Approved PO delivers 60 x cost 10 -> Inventory 600 / GRNI 600
        var receipt = await WithReceiptServiceAsync(tenantId, s => s.PostGoodsReceiptAsync(
            new PostGoodsReceiptRequest(
                env.CompanyId,
                env.PurchaseOrderId,
                env.LocationId,
                "core-rcpt-1",
                [new PostGoodsReceiptLine(env.PurchaseOrderDetailId, 60)]),
            env.MutationScope, Actor));

        receipt.Replayed.Should().BeFalse();
        receipt.AcceptedValue.Should().Be(600m);
        receipt.JournalNumber.Should().StartWith("JV");

        await using (var inspect = _fixture.CreateContext(tenantId))
        {
            var receiptLine = await inspect.GoodsReceiptLines.SingleAsync();
            env = env with { GoodsReceiptLineId = receiptLine.Id };
        }

        // Step 3: Supplier bill matches the receipt -> GRNI 600 / AP 600
        var bill = await WithInvoiceServiceAsync(tenantId, s => s.PostPurchaseInvoiceAsync(
            new PostPurchaseInvoiceRequest(
                env.CompanyId,
                env.SupplierId,
                "SUP-BILL-001",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                "core-bill-1",
                [new PostPurchaseInvoiceLine(env.GoodsReceiptLineId)]),
            Actor));

        bill.Replayed.Should().BeFalse();
        bill.TotalValue.Should().Be(600m);
        bill.OpenValue.Should().Be(600m);

        // Step 4: Pay supplier 400 -> AP 400 / Bank 400; AP open 200
        var payment = await WithPaymentServiceAsync(tenantId, s => s.PostPaymentAsync(
            new PostPaymentRequest(
                env.CompanyId,
                PaymentEntryType.Pay,
                PaymentMethod.BankTransfer,
                "core-pay-1",
                400m,
                [new PostPaymentAllocation(bill.PurchaseInvoiceId, 400m)],
                SupplierId: env.SupplierId),
            Actor));

        payment.Replayed.Should().BeFalse();
        payment.AllocatedValue.Should().Be(400m);

        // Step 5: Deliver/sell 10 x cost 10 -> COGS 100 / Inventory 100
        var order = await WithSalesServiceAsync(tenantId, s => s.PostSalesOrderAsync(
            new PostSalesOrderRequest(
                env.CompanyId,
                env.CustomerId,
                "core-so-1",
                [new PostSalesOrderLine(env.ItemId, 10, 15m)]),
            Actor));

        order.FirstLineId.Should().BeGreaterThan(0);

        var delivery = await WithSalesServiceAsync(tenantId, s => s.PostDeliveryAsync(
            new PostDeliveryRequest(
                env.CompanyId,
                order.SalesOrderId,
                env.LocationId,
                "core-del-1",
                [new PostDeliveryLine(order.FirstLineId, 10)]),
            env.MutationScope, Actor));

        delivery.Replayed.Should().BeFalse();
        delivery.CostOfGoodsSold.Should().Be(100m);

        // Step 6: Invoice customer 10 x price 15 = 150 -> AR 150 / Sales 150
        var customerInvoice = await WithSalesServiceAsync(tenantId, s => s.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                env.CompanyId,
                env.CustomerId,
                "CUST-INV-001",
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                "core-sinv-1",
                [new PostSalesInvoiceLine(delivery.FirstLineId)]),
            Actor));

        customerInvoice.Replayed.Should().BeFalse();
        customerInvoice.TotalValue.Should().Be(150m);
        customerInvoice.OpenValue.Should().Be(150m);

        // Step 7: Receive customer payment 100 -> Bank 100 / AR 100; AR open 50
        var customerReceipt = await WithPaymentServiceAsync(tenantId, s => s.PostPaymentAsync(
            new PostPaymentRequest(
                env.CompanyId,
                PaymentEntryType.Receive,
                PaymentMethod.BankTransfer,
                "core-rcpt-pay-1",
                100m,
                [new PostPaymentAllocation(customerInvoice.SalesInvoiceId, 100m)],
                CustomerId: env.CustomerId),
            Actor));

        customerReceipt.Replayed.Should().BeFalse();
        customerReceipt.AllocatedValue.Should().Be(100m);

        // ================= ASSERT EXACT CLOSING POSITIONS =================
        await using var verify = _fixture.CreateContext(tenantId);

        // 1. Stock: 50 units remaining at 500 value
        var totalStock = await verify.StockInHand.SumAsync(s => s.Quantity);
        totalStock.Should().Be(50, "60 received - 10 delivered = 50 units");

        var valuationBucket = await verify.StockValuationBuckets
            .SingleAsync(b => b.ItemId == env.ItemId && b.LocationId == env.LocationId);
        valuationBucket.Quantity.Should().Be(50);
        valuationBucket.Value.Should().Be(500m, "50 units at cost 10");

        // 2. Open Payables: 200
        var openAp = await WithInvoiceServiceAsync(tenantId, s => s.GetPayablesOpenAsync(env.CompanyId));
        openAp.TotalOpenValue.Should().Be(200m);
        openAp.Items.Single().OpenValue.Should().Be(200m);

        // 3. Open Receivables: 50
        var dbCustInv = await verify.SalesInvoices.SingleAsync(i => i.Id == customerInvoice.SalesInvoiceId);
        dbCustInv.TotalValue.Should().Be(150m);
        var allocated = await verify.PaymentAllocations
            .Where(a => a.SalesInvoiceId == customerInvoice.SalesInvoiceId)
            .SumAsync(a => a.Amount);
        allocated.Should().Be(100m);
        (dbCustInv.TotalValue - allocated).Should().Be(50m, "150 invoiced less 100 customer payment allocated");

        // 4. Trial Balance: Balanced
        var tb = await GetTrialBalanceAsync(tenantId, env.CompanyId);
        tb.TotalDebit.Should().Be(tb.TotalCredit, "GL kernel invariant: debits must equal credits");

        var bankRow = tb.Rows.Single(r => r.AccountCode == "1000");
        (bankRow.Debit - bankRow.Credit).Should().Be(-300m, "Paid 400 to supplier, received 100 from customer");

        var arRow = tb.Rows.Single(r => r.AccountCode == "1100");
        (arRow.Debit - arRow.Credit).Should().Be(50m, "Invoiced 150 less 100 received");

        var inventoryRow = tb.Rows.Single(r => r.AccountCode == "1300");
        (inventoryRow.Debit - inventoryRow.Credit).Should().Be(500m, "Received 600 less 100 COGS issued");

        var grniRow = tb.Rows.Single(r => r.AccountCode == "2100");
        (grniRow.Debit - grniRow.Credit).Should().Be(0m, "GRNI fully cleared");

        var apRow = tb.Rows.Single(r => r.AccountCode == "2000");
        (apRow.Credit - apRow.Debit).Should().Be(200m, "600 billed less 400 paid");

        var salesRow = tb.Rows.Single(r => r.AccountCode == "4000");
        salesRow.Credit.Should().Be(150m, "Total sales revenue");

        var cogsRow = tb.Rows.Single(r => r.AccountCode == "5000");
        cogsRow.Debit.Should().Be(100m, "Total cost of goods sold");

        // 5. Multi-tenant isolation: Second tenant cannot observe this data
        var tenant2 = $"other-tenant-{Guid.NewGuid():N}";
        await using var verifyOther = _fixture.CreateContext(tenant2);
        (await verifyOther.SalesInvoices.CountAsync()).Should().Be(0);
        (await verifyOther.GoodsReceipts.CountAsync()).Should().Be(0);
        (await verifyOther.StockInHand.CountAsync()).Should().Be(0);
    }

    private async Task<T> WithReceiptServiceAsync<T>(string tenantId, Func<GoodsReceiptService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
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
        var service = new GoodsReceiptService(
            context, unitOfWork, stockService,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<GoodsReceiptService>.Instance);

        return await action(service);
    }

    private async Task<T> WithInvoiceServiceAsync<T>(string tenantId, Func<PurchaseInvoiceService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        var service = new PurchaseInvoiceService(
            context,
            unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PurchaseInvoiceService>.Instance);

        return await action(service);
    }

    private async Task<T> WithPaymentServiceAsync<T>(string tenantId, Func<PaymentService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        var service = new PaymentService(
            context,
            unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PaymentService>.Instance);

        return await action(service);
    }

    private async Task<T> WithSalesServiceAsync<T>(string tenantId, Func<SalesService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
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
        var service = new SalesService(
            context, unitOfWork, stockService,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<SalesService>.Instance);

        return await action(service);
    }

    private async Task<TrialBalanceResult> GetTrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        var posting = new JournalPostingService(context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance);
        return await posting.GetTrialBalanceAsync(companyId, null, null);
    }

    private async Task<CoreScenarioEnv> CreateEnvironmentAsync(string tenantId)
    {
        int companyId;
        int locationId;
        int customerId;
        int supplierId;
        int itemId;
        int purchaseOrderId;
        int purchaseOrderDetailId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "SC-MAIN", LegalName = "Scenario Corp", BaseCurrency = "USD", CurrencyScale = 2, IsActive = true
            };
            var branch = new Branch { Code = "SC-BR", Name = "Main HQ", TimeZoneId = "UTC" };
            var location = new Location { Name = "Main warehouse" };
            var customer = new Customer { CustomerCode = "CUST-SC", Name = "Scenario Customer", IsActive = true };
            var supplier = new Supplier { Name = "Scenario Supplier", IsDeleted = false };
            var item = new Item { ItemCode = "ITEM-SC", Description = "Scenario Item", Rate = 15m, IsActive = true };

            branch.Company = company;
            location.Branch = branch;
            customer.Company = company;

            setup.AddRange(company, branch, location, customer, supplier, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            locationId = location.Id;
            customerId = customer.Id;
            supplierId = supplier.Id;
            itemId = item.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1100", Name = "Accounts Receivable", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "GRNI", AccountType = "Liability", AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", AccountClass = AccountClass.Income, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5000", Name = "Cost of Goods Sold", AccountType = "Expense", AccountClass = AccountClass.Expense, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);

            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            var mappings = new[]
            {
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsReceivable, ChartOfAccountId = ByCode("1100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsPayable, ChartOfAccountId = ByCode("2000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced, ChartOfAccountId = ByCode("2100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalesRevenue, ChartOfAccountId = ByCode("4000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CostOfGoodsSold, ChartOfAccountId = ByCode("5000").Id, IsActive = true }
            };
            setup.CompanyAccountMappings.AddRange(mappings);
            await setup.SaveChangesAsAsync(Actor);

            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                PONumber = "PO-SC-001",
                OrderDate = DateTime.UtcNow,
                CurrencyScale = 2,
                CalculationVersion = 1,
                CommercialVersion = 1,
                ApprovedCommercialVersion = 1,
                ApprovedCommercialSnapshotJson = "{}",
                TotalAmount = 600m,
                NetAmount = 600m,
                Status = PurchaseOrderStatus.Approved,
                OrderDetails =
                [
                    new OrderDetail
                    {
                        ItemId = itemId,
                        Quantity = 60,
                        UnitPrice = 10m,
                        CurrencyScale = 2,
                        CalculationVersion = 1,
                        NetAmount = 600m,
                        GrossAmount = 600m
                    }
                ]
            };
            setup.PurchaseOrders.Add(po);
            var detail = po.OrderDetails.Single();
            var doc = DocumentIdentity.Create(
                po.DocumentId, tenantId, companyId, "PurchaseOrder", po.PONumber, DateTime.UtcNow.Year, DocumentLifecycleStatus.Active, Actor);
            var docLine = DocumentLineIdentity.Create(
                detail.DocumentLineId, doc.Id, tenantId, companyId, "PurchaseOrderLine");
            setup.DocumentIdentities.Add(doc);
            setup.DocumentLineIdentities.Add(docLine);
            await setup.SaveChangesAsAsync(Actor);

            purchaseOrderId = po.Id;
            purchaseOrderDetailId = detail.Id;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var calCtx = _fixture.CreateContext(tenantId))
        {
            var calendar = new FiscalCalendarService(calCtx, new UnitOfWork(calCtx), NullLogger<FiscalCalendarService>.Instance);
            await calendar.CreateFiscalYearAsync(
                new CreateFiscalYearRequest(
                    companyId, $"FY{today.Year}", new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
                Actor);
        }

        return new CoreScenarioEnv(
            companyId,
            locationId,
            customerId,
            supplierId,
            itemId,
            purchaseOrderId,
            purchaseOrderDetailId,
            0,
            new StockMutationScope(companyId, () => Task.FromResult(true)));
    }

    private sealed record CoreScenarioEnv(
        int CompanyId,
        int LocationId,
        int CustomerId,
        int SupplierId,
        int ItemId,
        int PurchaseOrderId,
        int PurchaseOrderDetailId,
        int GoodsReceiptLineId,
        StockMutationScope MutationScope);
}
