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
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for payment advances (party-level open-balance math) and internal
/// transfers. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class PaymentAdvancesTransfersPostgreSqlTests
{
    private const string Actor = "advance-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public PaymentAdvancesTransfersPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Advance_ReducesPartyOpenBalance_BlocksOverAllocation()
    {
        var tenantId = $"adv-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithInvoiceAsync(tenantId, invoiceValue: 400m);

        // Pay a 250 advance before the invoice is allocated: party open drops 400 → 150.
        await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            new PostPaymentRequest(environment.CompanyId, PaymentEntryType.Pay, PaymentMethod.BankTransfer,
                "adv-1", 250m, [], SupplierId: environment.SupplierId, IsAdvance: true), Actor));

        var payables = await WithInvoiceServiceAsync(tenantId, service =>
            service.GetPayablesOpenAsync(environment.CompanyId));
        payables.TotalOpenValue.Should().Be(400m, "open items are per invoice; advances reduce the party balance");

        // Allocating 300 against the 400 invoice fails: only 150 headroom after the advance.
        var over = () => WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            new PostPaymentRequest(environment.CompanyId, PaymentEntryType.Pay, PaymentMethod.BankTransfer,
                "pay-1", 300m, [new PostPaymentAllocation(environment.PurchaseInvoiceId, 300m)],
                SupplierId: environment.SupplierId), Actor));
        (await over.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*party's open balance*");

        // 150 allocates cleanly.
        var payment = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            new PostPaymentRequest(environment.CompanyId, PaymentEntryType.Pay, PaymentMethod.BankTransfer,
                "pay-2", 150m, [new PostPaymentAllocation(environment.PurchaseInvoiceId, 150m)],
                SupplierId: environment.SupplierId), Actor));
        payment.Replayed.Should().BeFalse();

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        var payablesRow = trialBalance.Rows.Single(row => row.AccountCode == "2000");
        (payablesRow.Credit - payablesRow.Debit).Should().Be(0m, "400 credited, 250 advance + 150 allocation debited");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);
    }

    [PostgreSqlFact]
    public async Task Advance_RejectsAllocations_AndTransfersRejectSameAccount()
    {
        var tenantId = $"adv-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithInvoiceAsync(tenantId, invoiceValue: 100m);

        var advanceWithAllocation = () => WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            new PostPaymentRequest(environment.CompanyId, PaymentEntryType.Pay, PaymentMethod.BankTransfer,
                "adv-x", 50m, [new PostPaymentAllocation(environment.PurchaseInvoiceId, 50m)],
                SupplierId: environment.SupplierId, IsAdvance: true), Actor));
        (await advanceWithAllocation.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*without allocations*");

        var bank = await _fixture.CreateContext(tenantId).ChartOfAccounts
            .Where(account => account.CompanyId == environment.CompanyId && account.AccountCode == "1000")
            .SingleAsync();
        var same = () => WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(
            new PostPaymentRequest(environment.CompanyId, PaymentEntryType.InternalTransfer, PaymentMethod.BankTransfer,
                "trf-x", 40m, [], FromAccountId: bank.Id, ToAccountId: bank.Id), Actor));
        (await same.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*two different accounts*");
    }

    [PostgreSqlFact]
    public async Task InternalTransfer_MovesMoneyBetweenAccounts_ExactlyOnce()
    {
        var tenantId = $"adv-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentWithInvoiceAsync(tenantId, invoiceValue: 0m);
        var accounts = await _fixture.CreateContext(tenantId).ChartOfAccounts
            .Where(account => account.CompanyId == environment.CompanyId &&
                (account.AccountCode == "1000" || account.AccountCode == "1020"))
            .ToDictionaryAsync(account => account.AccountCode, account => account.Id);

        var transferRequest = new PostPaymentRequest(environment.CompanyId, PaymentEntryType.InternalTransfer,
            PaymentMethod.BankTransfer, "trf-1", 75m, [],
            FromAccountId: accounts["1000"], ToAccountId: accounts["1020"]);
        var first = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(transferRequest, Actor));
        var second = await WithPaymentServiceAsync(tenantId, service => service.PostPaymentAsync(transferRequest, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.PaymentEntryId.Should().Be(first.PaymentEntryId);

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        trialBalance.Rows.Single(row => row.AccountCode == "1020").Debit.Should().Be(75m, "cash drawer receives");
        trialBalance.Rows.Single(row => row.AccountCode == "1000").Credit.Should().Be(75m, "bank sends");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);
    }

    private static PaymentService BuildPaymentService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new PaymentService(
            context, unitOfWork,
            new JournalPostingService(context, unitOfWork, fiscalCalendar,
                new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            NullLogger<PaymentService>.Instance);
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

    private async Task<TrialBalanceResult> TrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var posting = new JournalPostingService(context, unitOfWork, fiscalCalendar,
            new DocumentNumberService(context, unitOfWork), NullLogger<JournalPostingService>.Instance);
        return await posting.GetTrialBalanceAsync(companyId, null, null);
    }

    /// <summary>Company with AP/Bank/Cash accounts, an optional invoice, and the posting policy.</summary>
    private async Task<TestEnvironment> CreateEnvironmentWithInvoiceAsync(string tenantId, decimal invoiceValue)
    {
        int companyId;
        int supplierId;
        int purchaseInvoiceId = 0;
        int receiptLineId = 0;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "ADV-MAIN", LegalName = "Advance test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "ADV-BR", Name = "Main", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"ADV-{Guid.NewGuid():N}"[..20], Description = "Advance item",
                Rate = 10m, IsActive = true
            };
            var location = new Location { Name = "Main warehouse" };
            var supplier = new Supplier { Name = "Advance supplier" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, item, location, supplier);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            supplierId = supplier.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1020", Name = "Cash on Hand", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "GRNI", AccountType = "Liability", IsGroupAccount = false, AccountClass = AccountClass.Liability, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, AccountClass = AccountClass.Asset, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CashOnHand, ChartOfAccountId = ByCode("1020").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.AccountsPayable, ChartOfAccountId = ByCode("2000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced, ChartOfAccountId = ByCode("2100").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true });
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

        if (invoiceValue > 0)
        {
            // One received-and-invoiced line at 10.00 per unit, through the real services.
            var units = (int)(invoiceValue / 10m);
            int purchaseOrderId;
            int purchaseOrderLineId;
            await using (var setup = _fixture.CreateContext(tenantId))
            {
                var item = await setup.Items.SingleAsync(candidate => candidate.ItemCode.StartsWith("ADV-"));
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
                    TotalAmount = invoiceValue,
                    NetAmount = invoiceValue,
                    OrderDetails =
                    [
                        new OrderDetail
                        {
                            ItemId = item.Id,
                            Quantity = units,
                            UnitPrice = 10m,
                            CurrencyScale = 2,
                            CalculationVersion = 1,
                            NetAmount = invoiceValue,
                            GrossAmount = invoiceValue
                        }
                    ]
                };
                setup.PurchaseOrders.Add(order);
                var detail = order.OrderDetails.Single();
                var document = DocumentIdentity.Create(
                    order.DocumentId, tenantId, companyId, "PurchaseOrder", order.PONumber,
                    DateTime.UtcNow.Year, DocumentLifecycleStatus.Active, "AdvanceTest");
                var lineIdentity = DocumentLineIdentity.Create(
                    detail.DocumentLineId, document.Id, tenantId, companyId, "PurchaseOrderLine");
                setup.DocumentIdentities.Add(document);
                setup.DocumentLineIdentities.Add(lineIdentity);
                await setup.SaveChangesAsAsync(Actor);
                purchaseOrderId = order.Id;
                purchaseOrderLineId = detail.Id;
            }

            await using (var p2pContext = _fixture.CreateContext(tenantId))
            {
                var unitOfWork = new UnitOfWork(p2pContext);
                var fiscalCalendar = new FiscalCalendarService(p2pContext, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
                var documentNumbers = new DocumentNumberService(p2pContext, unitOfWork);
                var webhookDispatcher = new Moq.Mock<IWebhookDispatcher>();
                webhookDispatcher
                    .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                var stockService = new StockService(
                    new Merconiq.Infrastructure.Repositories.Repository<StockInHand>(p2pContext),
                    new Merconiq.Infrastructure.Repositories.Repository<StockTransaction>(p2pContext),
                    new Merconiq.Infrastructure.Repositories.Repository<Item>(p2pContext),
                    new Merconiq.Infrastructure.Repositories.Repository<Location>(p2pContext),
                    new Merconiq.Infrastructure.Repositories.Repository<Branch>(p2pContext),
                    unitOfWork,
                    webhookDispatcher.Object,
                    new TestTenantContext(p2pContext.CurrentTenantId),
                    NullLogger<StockService>.Instance,
                    new Merconiq.Infrastructure.Repositories.Repository<StockValuationBucket>(p2pContext),
                    new Merconiq.Infrastructure.Repositories.Repository<StockValuationEntry>(p2pContext));
                var goodsReceipts = new GoodsReceiptService(
                    p2pContext, unitOfWork, stockService,
                    new JournalPostingService(p2pContext, unitOfWork, fiscalCalendar, documentNumbers,
                        NullLogger<JournalPostingService>.Instance),
                    new AccountPostingPolicyService(p2pContext),
                    fiscalCalendar,
                    documentNumbers,
                    NullLogger<GoodsReceiptService>.Instance);
                await goodsReceipts.PostGoodsReceiptAsync(
                    new PostGoodsReceiptRequest(
                        companyId, purchaseOrderId, (await p2pContext.Locations.SingleAsync()).Id, "adv-receipt",
                        [new PostGoodsReceiptLine(purchaseOrderLineId, units)]),
                    new StockMutationScope(companyId, () => Task.FromResult(true)),
                    Actor);
                var receiptLine = await p2pContext.GoodsReceiptLines.SingleAsync();
                receiptLineId = receiptLine.Id;

                var invoices = new PurchaseInvoiceService(
                    p2pContext, unitOfWork,
                    new JournalPostingService(p2pContext, unitOfWork, fiscalCalendar, documentNumbers,
                        NullLogger<JournalPostingService>.Instance),
                    new AccountPostingPolicyService(p2pContext),
                    fiscalCalendar,
                    NullLogger<PurchaseInvoiceService>.Instance);
                var invoice = await invoices.PostPurchaseInvoiceAsync(
                    new PostPurchaseInvoiceRequest(
                        companyId, supplierId, "ADV-INV-1",
                        today, today.AddDays(30), "adv-invoice",
                        [new PostPurchaseInvoiceLine(receiptLineId)]),
                    Actor);
                purchaseInvoiceId = invoice.PurchaseInvoiceId;
            }
        }

        return new TestEnvironment
        {
            CompanyId = companyId,
            SupplierId = supplierId,
            PurchaseInvoiceId = purchaseInvoiceId
        };
    }

    private sealed record TestEnvironment
    {
        public int CompanyId { get; init; }
        public int SupplierId { get; init; }
        public int PurchaseInvoiceId { get; init; }
    }
}
