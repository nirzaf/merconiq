using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for the G2 procurement slice: a posted goods receipt atomically books
/// valued stock intake and the balanced Inventory/GRNI journal, is idempotent per request key,
/// and never over-receives a purchase-order line. Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class GoodsReceiptPostgreSqlTests
{
    private const string Actor = "goods-receipt-tester";
    private const decimal OrderedPrice = 10m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public GoodsReceiptPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_BooksValuedStockAndGrniJournalAtomically()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        var request = Receipt(environment, accepted: 60);

        var result = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            request, environment.MutationScope, Actor));

        result.Replayed.Should().BeFalse();
        result.AcceptedQuantity.Should().Be(60);
        result.AcceptedValue.Should().Be(600m);
        result.JournalNumber.Should().StartWith("JV");
        result.ReceiptNumber.Should().StartWith("GR");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(60);
        (await verify.StockValuationBuckets.SumAsync(bucket => bucket.Value)).Should().Be(600m);

        var journal = await verify.JournalEntries.Include(entry => entry.Lines).SingleAsync(
            entry => entry.SourceDocumentType == "GoodsReceipt" &&
                entry.SourceDocumentId == result.GoodsReceiptId.ToString(),
            CancellationToken.None);
        journal.TotalDebit.Should().Be(600m).And.Be(journal.TotalCredit);
        journal.Lines.Single(line => line.Debit > 0).AccountCodeSnapshot.Should().Be("1300");
        journal.Lines.Single(line => line.Credit > 0).AccountCodeSnapshot.Should().Be("2100");

        var obligations = await WithPurchaseOrderServiceAsync(
            tenantId, service => service.GetLineObligationsAsync(environment.PurchaseOrderId));
        obligations!.ReceivedQuantity.Should().Be(60);
        obligations.OutstandingQuantity.Should().Be(40);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_SecondPartialReceiptCompletes_ThirdRejects()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        var first = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 60), environment.MutationScope, Actor));
        var second = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 40, key: "gr-key-2"), environment.MutationScope, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeFalse();

        var third = async () => await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 1, key: "gr-key-3"), environment.MutationScope, Actor));
        (await third.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*exceed the ordered*");

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(100);
        (await verify.GoodsReceipts.CountAsync()).Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_CommittedRequestKey_SamePayloadReplaysWithoutSecondEffect()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        var request = Receipt(environment, accepted: 60);

        var first = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            request, environment.MutationScope, Actor));
        var second = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            request, environment.MutationScope, Actor));

        first.Replayed.Should().BeFalse();
        second.Replayed.Should().BeTrue();
        second.GoodsReceiptId.Should().Be(first.GoodsReceiptId);
        second.JournalNumber.Should().Be(first.JournalNumber);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.GoodsReceipts.CountAsync()).Should().Be(1);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(60);
        (await verify.JournalEntries.CountAsync()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_CommittedRequestKey_ConflictingPayload_IsRejected()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 60), environment.MutationScope, Actor));

        var conflicting = async () => await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 70), environment.MutationScope, Actor));

        (await conflicting.Should().ThrowAsync<FinancePostingConflictException>())
            .WithMessage("*different payload*");
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_ConcurrentSameRequest_CreatesExactlyOneReceipt()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        var request = Receipt(environment, accepted: 60);

        async Task<GoodsReceiptResult> PostAsWorkerAsync(string worker) =>
            await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
                request, environment.MutationScope, worker));

        var first = PostAsWorkerAsync("worker-one");
        var second = PostAsWorkerAsync("worker-two");
        await Task.WhenAll(first, second);

        var results = new[] { await first, await second };
        results.Should().Contain(result => !result.Replayed);
        results.Select(result => result.GoodsReceiptId).Distinct().Should().HaveCount(1);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.GoodsReceipts.CountAsync()).Should().Be(1);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(60);
        (await verify.JournalEntries.CountAsync()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_NonApprovedOrder_IsRejected()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100, approve: false);

        var act = async () => await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 10), environment.MutationScope, Actor));

        (await act.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*Only approved purchase orders*");
        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.GoodsReceipts.CountAsync()).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_RejectedQuantity_IsRecordedButNeverStocked()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);

        var result = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 40, rejected: 10), environment.MutationScope, Actor));

        result.AcceptedQuantity.Should().Be(40);
        result.AcceptedValue.Should().Be(400m);

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(40);
        (await verify.StockValuationBuckets.SumAsync(bucket => bucket.Value)).Should().Be(400m);
        var line = await verify.GoodsReceiptLines.SingleAsync(CancellationToken.None);
        line.RejectedQuantity.Should().Be(10);
        line.BaseQuantity.Should().Be(40);

        var obligations = await WithPurchaseOrderServiceAsync(
            tenantId, service => service.GetLineObligationsAsync(environment.PurchaseOrderId));
        obligations!.ReceivedQuantity.Should().Be(40);
        obligations.OutstandingQuantity.Should().Be(50);
    }

    [PostgreSqlFact]
    public async Task PostGoodsReceipt_FailureAfterStockIntake_RollsBackStockAndReceiptTogether()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);

        // The journal insert happens after the stock movement; failing there must roll the
        // stock, valuation, receipt and journal back as one transaction.
        await using (var failingContext = _fixture.CreateContext(tenantId, "gr-faulted", new JournalFailureInterceptor()))
        {
            var service = BuildService(failingContext);
            var act = () => service.PostGoodsReceiptAsync(
                Receipt(environment, accepted: 60), environment.MutationScope, Actor);
            (await act.Should().ThrowAsync<DbUpdateException>()).Which.InnerException!.Message
                .Should().Contain("Injected journal failure");
        }

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.GoodsReceipts.CountAsync()).Should().Be(0);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(0);
        (await verify.StockValuationBuckets.CountAsync()).Should().Be(0);
        (await verify.JournalEntries.CountAsync()).Should().Be(0);

        var retry = await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 60), environment.MutationScope, Actor));
        retry.Replayed.Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task TrialBalance_AfterReceipts_ShowsInventoryAndGrni()
    {
        var tenantId = $"gr-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId, orderedQuantity: 100);
        await WithServiceAsync(tenantId, service => service.PostGoodsReceiptAsync(
            Receipt(environment, accepted: 60), environment.MutationScope, Actor));

        await using var verify = _fixture.CreateContext(tenantId);
        var posting = BuildPostingService(verify);
        var trialBalance = await posting.GetTrialBalanceAsync(environment.CompanyId, null, null);
        var inventory = trialBalance.Rows.Single(row => row.AccountCode == "1300");
        inventory.Debit.Should().Be(600m);
        var grni = trialBalance.Rows.Single(row => row.AccountCode == "2100");
        grni.Credit.Should().Be(600m);
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);
    }

    private static JournalPostingService BuildPostingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
    }

    private static PostGoodsReceiptRequest Receipt(
        TestEnvironment environment,
        int accepted,
        int rejected = 0,
        string key = "gr-key-1") => new(
        environment.CompanyId,
        environment.PurchaseOrderId,
        environment.LocationId,
        key,
        [new PostGoodsReceiptLine(environment.PurchaseOrderLineId, accepted, rejected)]);

    private static GoodsReceiptService BuildService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var webhookDispatcher = new Mock<IWebhookDispatcher>();
        webhookDispatcher
            .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var tenantContext = new TestTenantContext(context.CurrentTenantId);
        var stockService = new StockService(
            new Repository<StockInHand>(context),
            new Repository<StockTransaction>(context),
            new Repository<Item>(context),
            new Repository<Location>(context),
            new Repository<Branch>(context),
            unitOfWork,
            webhookDispatcher.Object,
            tenantContext,
            NullLogger<StockService>.Instance,
            new Repository<StockValuationBucket>(context),
            new Repository<StockValuationEntry>(context));
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var documentNumbers = new DocumentNumberService(context, unitOfWork);
        var postingPolicy = new AccountPostingPolicyService(context);
        var journalPosting = new JournalPostingService(
            context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance);
        return new GoodsReceiptService(
            context, unitOfWork, stockService, journalPosting, postingPolicy, fiscalCalendar,
            documentNumbers, NullLogger<GoodsReceiptService>.Instance);
    }

    private static IPurchaseOrderService BuildPurchaseOrderService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var webhookDispatcher = new Mock<IWebhookDispatcher>();
        webhookDispatcher
            .Setup(dispatcher => dispatcher.EnqueueAsync(It.IsAny<WebhookEvent<object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var documentIdentityService = new DocumentIdentityService(
            context, unitOfWork, new DocumentNumberService(context, unitOfWork));
        return new PurchaseOrderService(
            new Repository<PurchaseOrder>(context),
            unitOfWork,
            documentIdentityService,
            webhookDispatcher.Object,
            new TestTenantContext(context.CurrentTenantId),
            NullLogger<PurchaseOrderService>.Instance,
            new Repository<AuditLog>(context),
            orderDetailRepository: new Repository<OrderDetail>(context),
            itemRepository: new Repository<Item>(context),
            goodsReceiptLineRepository: new Repository<GoodsReceiptLine>(context));
    }

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<GoodsReceiptService, Task<T>> action)
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

    private async Task<T> WithPurchaseOrderServiceAsync<T>(string tenantId, Func<IPurchaseOrderService, Task<T>> action)
    {
        var context = _fixture.CreateContext(tenantId);
        try
        {
            return await action(BuildPurchaseOrderService(context));
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TestEnvironment> CreateEnvironmentAsync(
        string tenantId,
        int orderedQuantity,
        bool approve = true)
    {
        int companyId;
        int purchaseOrderId;
        int purchaseOrderLineId;
        int locationId;
        int inventoryAccountId;
        int grniAccountId;
        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company
            {
                Code = "GR-MAIN",
                LegalName = "Goods receipt test company",
                BaseCurrency = "USD",
                IsActive = true
            };
            var branch = new Branch { Code = "GR-BR", Name = "Receiving branch", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"GR-ITEM-{Guid.NewGuid():N}"[..20],
                Description = "Receipt test item",
                Rate = OrderedPrice,
                IsActive = true
            };
            var location = new Location { Name = "Receiving dock" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, item, location);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            locationId = location.Id;

            var supplier = new Supplier { Name = "Receipt test supplier" };
            setup.Suppliers.Add(supplier);
            await setup.SaveChangesAsAsync(Actor);

            // The PO/detail constructors mint their document identities; create the matching
            // identity rows from those generated values so the FK graph lines up.
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
            // Identity rows must exist before the PO row that references them.
            var detail = order.OrderDetails.Single();
            var document = DocumentIdentity.Create(
                order.DocumentId,
                tenantId,
                companyId,
                "PurchaseOrder",
                order.PONumber,
                DateTime.UtcNow.Year,
                DocumentLifecycleStatus.Active,
                "GoodsReceiptTest");
            var lineIdentity = DocumentLineIdentity.Create(
                detail.DocumentLineId, document.Id, tenantId, companyId, "PurchaseOrderLine");
            setup.DocumentIdentities.Add(document);
            setup.DocumentLineIdentities.Add(lineIdentity);
            await setup.SaveChangesAsAsync(Actor);

            setup.PurchaseOrders.Add(order);
            await setup.SaveChangesAsAsync(Actor);
            purchaseOrderId = order.Id;
            purchaseOrderLineId = detail.Id;

            var inventoryAccount = new ChartOfAccount
            {
                CompanyId = companyId, AccountCode = "1300", Name = "Inventory",
                AccountType = "Asset", IsGroupAccount = false, IsActive = true
            };
            var grniAccount = new ChartOfAccount
            {
                CompanyId = companyId, AccountCode = "2100", Name = "Goods Received Not Invoiced",
                AccountType = "Liability", IsGroupAccount = false, IsActive = true
            };
            setup.ChartOfAccounts.AddRange(inventoryAccount, grniAccount);
            await setup.SaveChangesAsAsync(Actor);
            inventoryAccountId = inventoryAccount.Id;
            grniAccountId = grniAccount.Id;

            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping
                {
                    CompanyId = companyId, Role = AccountPostingRole.Inventory,
                    ChartOfAccountId = inventoryAccountId, IsActive = true
                },
                new CompanyAccountMapping
                {
                    CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced,
                    ChartOfAccountId = grniAccountId, IsActive = true
                });
            await setup.SaveChangesAsAsync(Actor);

            if (!approve)
            {
                order.Status = PurchaseOrderStatus.Submitted;
                await setup.SaveChangesAsAsync(Actor);
            }
        }

        // A fiscal year is required for the posting-date gate.
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

        return new TestEnvironment
        {
            CompanyId = companyId,
            PurchaseOrderId = purchaseOrderId,
            PurchaseOrderLineId = purchaseOrderLineId,
            LocationId = locationId,
            MutationScope = new StockMutationScope(companyId, () => Task.FromResult(true))
        };
    }

    private sealed class TestEnvironment
    {
        public int CompanyId { get; init; }
        public int PurchaseOrderId { get; init; }
        public int PurchaseOrderLineId { get; init; }
        public int LocationId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }

    /// <summary>Fails the journal phase after the stock movement, inside the same transaction.</summary>
    private sealed class JournalFailureInterceptor : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"JournalEntries\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected journal failure inside the receipt transaction.");
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
