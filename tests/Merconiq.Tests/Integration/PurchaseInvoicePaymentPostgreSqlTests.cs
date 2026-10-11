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
/// Relational acceptance for the G3 slice: supplier invoices matched to posted receipts clear
/// GRNI exactly once and recognize the payable, and payments allocate open invoices without ever
/// over-allocating, even under concurrency. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PurchaseInvoicePaymentPostgreSqlTests
{
    private const string Actor = "p2p-tester";
    private const decimal OrderedPrice = 10m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public PurchaseInvoicePaymentPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task PostInvoice_MatchesReceipt_ClearsGrniAndCreditsAp()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);

        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        invoice.Replayed.Should().BeFalse();
        invoice.TotalValue.Should().Be(600m);
        invoice.OpenValue.Should().Be(600m);
        invoice.JournalNumber.Should().StartWith("JV");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(60,
            "invoicing must never touch stock");
        var posting = BuildPostingService(verify);
        var trialBalance = await posting.GetTrialBalanceAsync(environment.CompanyId, null, null);
        trialBalance.Rows.Single(row => row.AccountCode == "2100").Should().Match<TrialBalanceRow>(
            grni => grni.Debit == 600m && grni.Credit == 600m,
            "GRNI accrual and clearing net to zero");
        trialBalance.Rows.Single(row => row.AccountCode == "2000").Credit.Should().Be(600m);

        var payables = await WithInvoiceServiceAsync(
            tenantId, service => service.GetPayablesOpenAsync(environment.CompanyId));
        payables.TotalOpenValue.Should().Be(600m);
    }

    [PostgreSqlFact]
    public async Task PostInvoice_DuplicateSupplierInvoiceNumber_Conflicts()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(
            tenantId, orderedQuantity: 100, accepted: 60, secondReceiptAccepted: 40);
        await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId, invoiceNumber: "SUP-INV-DUPLICATE"), Actor));

        var duplicate = async () => await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.SecondGoodsReceiptLineId!.Value, key: "pinv-dup", invoiceNumber: "SUP-INV-DUPLICATE"), Actor));

        (await duplicate.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already exists for this supplier*");
    }

    [PostgreSqlFact]
    public async Task PostInvoice_DoubleMatchingReceiptLine_Conflicts()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        var second = async () => await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId, key: "pinv-2"), Actor));

        (await second.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*already matched by a posted invoice*");
    }

    [PostgreSqlFact]
    public async Task PostInvoice_CommittedRequestKey_SamePayloadReplays()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var request = Invoice(environment, environment.GoodsReceiptLineId);

        var first = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(request, Actor));
        var second = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(request, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.PurchaseInvoiceId.Should().Be(first.PurchaseInvoiceId);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.PurchaseInvoices.CountAsync()).Should().Be(1);
        (await verify.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "PurchaseInvoice"))
            .Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task PostPayment_PartiallyPaysInvoice_UpdatesOpenBalanceAndBank()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        var payment = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            Payment(environment, invoice.PurchaseInvoiceId, amount: 400m), Actor));

        payment.Replayed.Should().BeFalse();
        payment.AllocatedValue.Should().Be(400m);
        payment.JournalNumber.Should().StartWith("JV");

        await using var verify = _fixture.CreateContext(tenantId);
        var posting = BuildPostingService(verify);
        var trialBalance = await posting.GetTrialBalanceAsync(environment.CompanyId, null, null);
        var payablesRow = trialBalance.Rows.Single(row => row.AccountCode == "2000");
        payablesRow.Debit.Should().Be(400m);
        payablesRow.Credit.Should().Be(600m);
        (payablesRow.Credit - payablesRow.Debit).Should().Be(200m, "net AP position after the payment");
        trialBalance.Rows.Single(row => row.AccountCode == "1000").Credit.Should().Be(400m);

        var openPayables = await WithInvoiceServiceAsync(
            tenantId, service => service.GetPayablesOpenAsync(environment.CompanyId));
        openPayables.TotalOpenValue.Should().Be(200m);
        openPayables.Items.Single().PaidValue.Should().Be(400m);
    }

    [PostgreSqlFact]
    public async Task PostPayment_OverAllocation_IsRejected()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        var over = async () => await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            Payment(environment, invoice.PurchaseInvoiceId, amount: 700m), Actor));

        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*exceeds its open balance*");
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.PaymentEntries.CountAsync()).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task PostPayment_UnallocatedOrPartialAmount_IsRejected()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        var partial = async () => await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            Payment(environment, invoice.PurchaseInvoiceId, amount: 400m, allocated: 100m), Actor));

        (await partial.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*must be fully allocated*");
    }

    [PostgreSqlFact]
    public async Task PostPayment_ConcurrentOverAllocation_OnlyOnePaymentWins()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));

        async Task<PaymentResult> PayAsync(string key) => await WithPaymentServiceAsync(
            tenantId, service => service.PostPaymentAsync(
                Payment(environment, invoice.PurchaseInvoiceId, amount: 400m, key: key), Actor));

        // Both payments race the same 600 open balance; the advisory lock serializes the
        // open-balance checks, so exactly one can allocate 400.
        var first = PayAsync("pay-1");
        var second = PayAsync("pay-2");
        PaymentResult? firstResult = null;
        PaymentResult? secondResult = null;
        var failures = new List<Exception>();
        try { firstResult = await first; } catch (Exception error) { failures.Add(error); }
        try { secondResult = await second; } catch (Exception error) { failures.Add(error); }

        ((firstResult is not null) ^ (secondResult is not null)).Should().BeTrue(
            "exactly one concurrent payment fits the open balance");
        failures.Should().ContainSingle().Which.Should().BeOfType<FinancePostingValidationException>();

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.PaymentEntries.CountAsync()).Should().Be(1);
        (await verify.PaymentAllocations.SumAsync(allocation => allocation.Amount)).Should().Be(400m);
    }

    [PostgreSqlFact]
    public async Task PostPayment_CommittedRequestKey_Replays_WithoutSecondJournal()
    {
        var tenantId = $"p2p-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithReceiptAsync(tenantId, orderedQuantity: 100, accepted: 60);
        var invoice = await WithInvoiceServiceAsync(tenantId, service => service.PostPurchaseInvoiceAsync(
            Invoice(environment, environment.GoodsReceiptLineId), Actor));
        var request = Payment(environment, invoice.PurchaseInvoiceId, amount: 400m);

        var first = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(request, Actor));
        var second = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(request, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.PaymentEntryId.Should().Be(first.PaymentEntryId);
        second.JournalNumber.Should().Be(first.JournalNumber);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.PaymentEntries.CountAsync()).Should().Be(1);
        (await verify.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "PaymentEntry"))
            .Should().Be(1);
        (await verify.PaymentAllocations.SumAsync(allocation => allocation.Amount)).Should().Be(400m);
    }

    private static PostPurchaseInvoiceRequest Invoice(
        TestEnvironment environment,
        int goodsReceiptLineId,
        string key = "pinv-key-1",
        string? invoiceNumber = null) => new(
        environment.CompanyId,
        environment.SupplierId,
        invoiceNumber ?? $"SUP-INV-{key}",
        DateOnly.FromDateTime(DateTime.UtcNow),
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
        key,
        [new PostPurchaseInvoiceLine(goodsReceiptLineId)]);

    private static PostPaymentRequest Payment(
        TestEnvironment environment,
        int purchaseInvoiceId,
        decimal amount,
        decimal? allocated = null,
        string key = "pay-key-1") => new(
        environment.CompanyId,
        PaymentEntryType.Pay,
        PaymentMethod.BankTransfer,
        key,
        amount,
        [new PostPaymentAllocation(purchaseInvoiceId, allocated ?? amount)],
        SupplierId: environment.SupplierId);

    private static JournalPostingService BuildPostingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
    }

    private static PurchaseInvoiceService BuildInvoiceService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PurchaseInvoiceService(
            context,
            unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PurchaseInvoiceService>.Instance);
    }

    private static PaymentService BuildPaymentService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PaymentService(
            context,
            unitOfWork,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
                NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PaymentService>.Instance);
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

    /// <summary>Seeds the P2P environment: company, chart, approved PO, and one posted receipt.</summary>
    private async Task<TestEnvironment> CreateEnvironmentWithReceiptAsync(
        string tenantId,
        int orderedQuantity,
        int accepted,
        int? secondReceiptAccepted = null)
    {
        int companyId;
        int supplierId;
        int locationId;
        int purchaseOrderId;
        int purchaseOrderLineId;
        int goodsReceiptLineId;
        int? secondGoodsReceiptLineId = null;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "P2P-MAIN", LegalName = "P2P test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "P2P-BR", Name = "Main branch", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"P2P-{Guid.NewGuid():N}"[..20], Description = "P2P item",
                Rate = OrderedPrice, IsActive = true
            };
            var location = new Location { Name = "Main warehouse" };
            var supplier = new Supplier { Name = "P2P supplier" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, item, location, supplier);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            supplierId = supplier.Id;
            locationId = location.Id;

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
                TotalAmount = orderedQuantity * OrderedPrice,
                NetAmount = orderedQuantity * OrderedPrice,
                OrderDetails =
                [
                    new OrderDetail
                    {
                        ItemId = item.Id,
                        Quantity = orderedQuantity,
                        UnitPrice = OrderedPrice,
                        CurrencyScale = 2,
                        CalculationVersion = 1,
                        NetAmount = orderedQuantity * OrderedPrice,
                        GrossAmount = orderedQuantity * OrderedPrice
                    }
                ]
            };
            setup.PurchaseOrders.Add(order);
            var detail = order.OrderDetails.Single();
            var document = DocumentIdentity.Create(
                order.DocumentId, tenantId, companyId, "PurchaseOrder", order.PONumber,
                DateTime.UtcNow.Year, DocumentLifecycleStatus.Active, "P2PTest");
            var lineIdentity = DocumentLineIdentity.Create(
                detail.DocumentLineId, document.Id, tenantId, companyId, "PurchaseOrderLine");
            setup.DocumentIdentities.Add(document);
            setup.DocumentLineIdentities.Add(lineIdentity);
            await setup.SaveChangesAsAsync(Actor);
            purchaseOrderId = order.Id;
            purchaseOrderLineId = detail.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "GRNI", AccountType = "Liability", IsGroupAccount = false, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
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

        // Post the receipt through the real G2 service so the invoice matches posted evidence.
        var receiptContext = _fixture.CreateContext(tenantId);
        var goodsReceiptService = BuildGoodsReceiptService(receiptContext, tenantId);
        try
        {
            var receipt = await goodsReceiptService.PostGoodsReceiptAsync(
                new PostGoodsReceiptRequest(
                    companyId, purchaseOrderId, locationId, "p2p-receipt-key",
                    [new PostGoodsReceiptLine(purchaseOrderLineId, accepted)]),
                new StockMutationScope(companyId, () => Task.FromResult(true)),
                Actor);
            goodsReceiptLineId = (await receiptContext.GoodsReceiptLines.SingleAsync()).Id;

            if (secondReceiptAccepted is { } secondAccepted)
            {
                await receiptContext.DisposeAsync();
                receiptContext = _fixture.CreateContext(tenantId);
                var secondService = BuildGoodsReceiptService(receiptContext, tenantId);
                await secondService.PostGoodsReceiptAsync(
                    new PostGoodsReceiptRequest(
                        companyId, purchaseOrderId, locationId, "p2p-receipt-key-2",
                        [new PostGoodsReceiptLine(purchaseOrderLineId, secondAccepted)]),
                    new StockMutationScope(companyId, () => Task.FromResult(true)),
                    Actor);
                secondGoodsReceiptLineId = (await receiptContext.GoodsReceiptLines
                    .OrderBy(line => line.Id).LastAsync()).Id;
            }
        }
        finally
        {
            await receiptContext.DisposeAsync();
        }

        return new TestEnvironment
        {
            CompanyId = companyId,
            SupplierId = supplierId,
            LocationId = locationId,
            PurchaseOrderId = purchaseOrderId,
            PurchaseOrderLineId = purchaseOrderLineId,
            GoodsReceiptLineId = goodsReceiptLineId,
            SecondGoodsReceiptLineId = secondGoodsReceiptLineId
        };
    }

    private static GoodsReceiptService BuildGoodsReceiptService(InventoryDbContext context, string tenantId)
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
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<GoodsReceiptService>.Instance);
    }

    private sealed class TestEnvironment
    {
        public int CompanyId { get; init; }
        public int SupplierId { get; init; }
        public int LocationId { get; init; }
        public int PurchaseOrderId { get; init; }
        public int PurchaseOrderLineId { get; init; }
        public int GoodsReceiptLineId { get; init; }
        public int? SecondGoodsReceiptLineId { get; init; }
    }
}
