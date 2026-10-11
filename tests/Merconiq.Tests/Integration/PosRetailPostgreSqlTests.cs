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
/// Relational acceptance for the retail slice: idempotent checkouts that issue valued stock and
/// split tenders into cash/bank journals with exact COGS, refunds bounded by the
/// sold-minus-refunded balance, and shift close with cash over/short posting.
/// Requires RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class PosRetailPostgreSqlTests
{
    private const string Actor = "pos-tester";
    private const decimal CostPerUnit = 10m;

    private readonly PostgreSqlIntegrationFixture _fixture;

    public PosRetailPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Checkout_WithMixedTenders_BooksRevenueAndCogsExactlyOnce()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Front till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, OpeningFloat: 100m), Actor);

            var sale = await service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 2, price: 15m, cash: 20m, card: 10m),
                environment.MutationScope,
                Actor);

            sale.Replayed.Should().BeFalse();
            sale.TotalValue.Should().Be(30m);
            sale.CostOfGoodsSold.Should().Be(20m);

            var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
            trialBalance.Rows.Single(row => row.AccountCode == "1000").Debit.Should().Be(10m, "card tender to bank");
            trialBalance.Rows.Single(row => row.AccountCode == "1020").Debit.Should().Be(20m, "cash tender to drawer");
            trialBalance.Rows.Single(row => row.AccountCode == "4000").Credit.Should().Be(30m, "revenue");
            trialBalance.Rows.Single(row => row.AccountCode == "5000").Debit.Should().Be(20m, "COGS");
            var inventory = trialBalance.Rows.Single(row => row.AccountCode == "1300");
            (inventory.Debit - inventory.Credit).Should().Be(580m, "600 opening less 20 issued");
            trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

            (await TrialBalanceAsync(tenantId, environment.CompanyId)).TotalDebit
                .Should().Be(trialBalance.TotalDebit);
            return sale;
        });

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(58);
    }

    [PostgreSqlFact]
    public async Task Checkout_CommittedRequestKey_Replays_WithoutSecondEffect()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, 0m), Actor);
            var request = Sale(environment, register.Id, shift.Id, quantity: 2, price: 15m, cash: 30m, card: 0m);

            var first = await service.PostSaleAsync(request, environment.MutationScope, Actor);
            var second = await service.PostSaleAsync(request, environment.MutationScope, Actor);

            first.Replayed.Should().BeFalse();
            second.Replayed.Should().BeTrue();
            second.PosSaleId.Should().Be(first.PosSaleId);
            second.CostOfGoodsSold.Should().Be(first.CostOfGoodsSold);
            return 0;
        });

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.PosSales.CountAsync()).Should().Be(1);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(58);
        (await verify.JournalEntries.CountAsync(entry => entry.SourceDocumentType == "PosSale")).Should().Be(2,
            "one revenue journal and one COGS journal, committed exactly once");
    }

    [PostgreSqlFact]
    public async Task Checkout_OnClosedShift_IsRejected()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, 0m), Actor);
            await service.CloseShiftAsync(
                new ClosePosShiftRequest(environment.CompanyId, shift.Id, CountedCash: 0m), Actor);

            var act = () => service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 1, price: 15m, cash: 15m, card: 0m),
                environment.MutationScope,
                Actor);
            (await act.Should().ThrowAsync<FinancePostingValidationException>())
                .WithMessage("*closed shift*");
            return 0;
        });
    }

    [PostgreSqlFact]
    public async Task Refund_IsBoundedBySoldBalance_PreventsDoubleRefund()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId, saleId, saleLineId) = await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, 50m), Actor);
            var sale = await service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 3, price: 15m, cash: 45m, card: 0m),
                environment.MutationScope,
                Actor);
            await using var lookup = _fixture.CreateContext(tenantId);
            var saleLine = await lookup.PosSaleLines.SingleAsync();
            return (register.Id, shift.Id, sale.PosSaleId, saleLine.Id);
        });

        await WithServiceAsync(tenantId, async service =>
        {
            // Refund 2 of 3 units: cash back 30, 20 of moving-average value restocked.
            var refund = await service.PostRefundAsync(
                new PostPosRefundRequest(
                    environment.CompanyId, registerId, shiftId, saleId, "refund-1", "Customer changed mind",
                    [new PostPosRefundLine(saleLineId, 2, StockReturnDisposition.Restockable)]),
                environment.MutationScope,
                Actor);

            refund.Replayed.Should().BeFalse();
            refund.TotalValue.Should().Be(30m);
            refund.RestockedValue.Should().Be(20m);

            // Refunding 2 more exceeds the refundable balance of 1.
            var doubleRefund = () => service.PostRefundAsync(
                new PostPosRefundRequest(
                    environment.CompanyId, registerId, shiftId, saleId, "refund-2", "Second attempt",
                    [new PostPosRefundLine(saleLineId, 2, StockReturnDisposition.Restockable)]),
                environment.MutationScope,
                Actor);
            (await doubleRefund.Should().ThrowAsync<FinancePostingValidationException>())
                .WithMessage("*refundable balance*");
            return 0;
        });

        await using var verify = _fixture.CreateContext(tenantId);
        (await verify.StockInHand.SumAsync(stock => stock.Quantity)).Should().Be(59, "60 sold 3, restocked 2");
        (await verify.PosRefunds.CountAsync()).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task ShiftClose_ReconcilesCash_AndPostsOverShortOnce()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, 100m), Actor);
            await service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 2, price: 15m, cash: 30m, card: 0m),
                environment.MutationScope,
                Actor);

            // Expected drawer: 100 float + 30 cash sale = 130. Count 125 → short 5.
            var close = await service.CloseShiftAsync(
                new ClosePosShiftRequest(environment.CompanyId, shift.Id, CountedCash: 125m), Actor);

            close.ExpectedCash.Should().Be(130m);
            close.OverShortValue.Should().Be(-5m);
            close.OverShortJournalNumber.Should().StartWith("JV");

            var again = () => service.CloseShiftAsync(
                new ClosePosShiftRequest(environment.CompanyId, shift.Id, CountedCash: 125m), Actor);
            (await again.Should().ThrowAsync<FinancePostingConflictException>())
                .WithMessage("*already closed*");

            var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
            trialBalance.Rows.Single(row => row.AccountCode == "9100").Should().Match<TrialBalanceRow>(
                row => row.Debit == 5m && row.Credit == 0m, "cash short expense");
            trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);
            return 0;
        });
    }

    [PostgreSqlFact]
    public async Task Receipt_ReflectsTheCommittedSaleLinesTotalsAndTenders()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var saleId = await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Front till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, OpeningFloat: 100m), Actor);
            var sale = await service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 2, price: 15m, cash: 20m, card: 10m),
                environment.MutationScope,
                Actor);
            return sale.PosSaleId;
        });

        await using var context = _fixture.CreateContext(tenantId);
        var receipt = await new PosReceiptService(context)
            .GetSaleReceiptAsync(environment.CompanyId, saleId);

        receipt.Should().NotBeNull();
        var item = await context.Items.SingleAsync(candidate => candidate.Id == environment.ItemId);
        receipt!.CompanyName.Should().Be("POS test company");
        receipt.RegisterName.Should().Be("Front till");
        receipt.Total.Should().Be(30m);
        receipt.Lines.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new PosReceiptLine(item.ItemCode, "Retail item", 2, 15m, 30m));
        receipt.Tenders.Select(tender => (tender.MethodName, tender.Amount)).Should().Equal(
            ("Cash", 20m),
            ("Bank transfer", 10m));

        var text = PosReceiptFormatter.Format(receipt);
        text.Should().Contain($"POS-{saleId}");
        text.Should().Contain(item.ItemCode);
        text.Split('\n').Should().OnlyContain(line => line.Length <= PosReceiptFormatter.DefaultWidth);
    }

    [PostgreSqlFact]
    public async Task Receipt_ForASaleOutsideTheTenant_IsAbsent()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var saleId = await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(
                environment.CompanyId, environment.LocationId, "Front till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, OpeningFloat: 100m), Actor);
            var sale = await service.PostSaleAsync(
                Sale(environment, register.Id, shift.Id, quantity: 1, price: 15m, cash: 15m, card: 0m),
                environment.MutationScope,
                Actor);
            return sale.PosSaleId;
        });

        await using var otherTenant = _fixture.CreateContext($"pos-other-{Guid.NewGuid():N}");
        var receipt = await new PosReceiptService(otherTenant)
            .GetSaleReceiptAsync(environment.CompanyId, saleId);

        receipt.Should().BeNull("a sale in another tenant must not be readable");
    }

    [PostgreSqlFact]
    public async Task CardSettlement_MatchesCardTendersAndReportsWhatIsStillAwaiting()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        var tenderId = await CardSaleTenderIdAsync(tenantId, environment, registerId, shiftId, quantity: 2, price: 15m, key: "card-1");

        var batch = await WithCardSettlementAsync(tenantId, service => service.ImportBatchAsync(
            new CardSettlementBatchImportRequest(environment.CompanyId, "PROC-BATCH-1", Today(), 30m, 0.5m), Actor));
        batch.NetAmount.Should().Be(29.5m);

        var before = await WithCardSettlementAsync(tenantId, service => service.GetReconciliationAsync(environment.CompanyId, batch.Id));
        before!.MatchedAmount.Should().Be(0m);
        before.UnmatchedGrossAmount.Should().Be(30m);
        before.IsFullySettled.Should().BeFalse();
        before.AwaitingTenders.Should().ContainSingle().Which.PosSaleTenderId.Should().Be(tenderId);

        await WithCardSettlementAsync(tenantId, service => service.MatchTenderAsync(environment.CompanyId, batch.Id, tenderId, Actor));

        var after = await WithCardSettlementAsync(tenantId, service => service.GetReconciliationAsync(environment.CompanyId, batch.Id));
        after!.MatchedAmount.Should().Be(30m);
        after.IsFullySettled.Should().BeTrue();
        after.AwaitingTenders.Should().BeEmpty();
    }

    [PostgreSqlFact]
    public async Task CardSettlement_RejectsOverSettlementCashTendersAndSettlementsBeforeTheSale()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        var firstTender = await CardSaleTenderIdAsync(tenantId, environment, registerId, shiftId, quantity: 2, price: 15m, key: "card-a");
        var secondTender = await CardSaleTenderIdAsync(tenantId, environment, registerId, shiftId, quantity: 1, price: 15m, key: "card-b");
        var cashTender = await CashSaleTenderIdAsync(tenantId, environment, registerId, shiftId, key: "cash-a");

        var batch = await WithCardSettlementAsync(tenantId, service => service.ImportBatchAsync(
            new CardSettlementBatchImportRequest(environment.CompanyId, "PROC-BATCH-2", Today(), 30m, 0m), Actor));
        await WithCardSettlementAsync(tenantId, service => service.MatchTenderAsync(environment.CompanyId, batch.Id, firstTender, Actor));

        var overSettle = async () => await WithCardSettlementAsync(tenantId, service =>
            service.MatchTenderAsync(environment.CompanyId, batch.Id, secondTender, Actor));
        (await overSettle.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*remaining gross*");

        var cash = async () => await WithCardSettlementAsync(tenantId, service =>
            service.MatchTenderAsync(environment.CompanyId, batch.Id, cashTender, Actor));
        (await cash.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*Only card tenders*");

        var earlier = await WithCardSettlementAsync(tenantId, service => service.ImportBatchAsync(
            new CardSettlementBatchImportRequest(environment.CompanyId, "PROC-BATCH-0", Today().AddDays(-1), 15m, 0m), Actor));
        var beforeSale = async () => await WithCardSettlementAsync(tenantId, service =>
            service.MatchTenderAsync(environment.CompanyId, earlier.Id, secondTender, Actor));
        (await beforeSale.Should().ThrowAsync<FinancePostingValidationException>())
            .WithMessage("*after the batch settlement date*");
    }

    [PostgreSqlFact]
    public async Task CardSettlement_UnmatchNeedsAReason_KeepsHistory_AndAllowsSettlingAgain()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        var tenderId = await CardSaleTenderIdAsync(tenantId, environment, registerId, shiftId, quantity: 1, price: 15m, key: "card-u");
        var batch = await WithCardSettlementAsync(tenantId, service => service.ImportBatchAsync(
            new CardSettlementBatchImportRequest(environment.CompanyId, "PROC-BATCH-3", Today(), 15m, 0m), Actor));
        await WithCardSettlementAsync(tenantId, service => service.MatchTenderAsync(environment.CompanyId, batch.Id, tenderId, Actor));

        var noReason = async () => await WithCardSettlementAsync(tenantId, service =>
            service.UnmatchTenderAsync(environment.CompanyId, batch.Id, tenderId, "  ", Actor));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        await WithCardSettlementAsync(tenantId, service =>
            service.UnmatchTenderAsync(environment.CompanyId, batch.Id, tenderId, "Processor reversed the batch line", "reviewer"));
        var withdrawn = await WithCardSettlementAsync(tenantId, service => service.GetReconciliationAsync(environment.CompanyId, batch.Id));
        withdrawn!.MatchedAmount.Should().Be(0m);
        withdrawn.AwaitingTenders.Should().ContainSingle();

        await WithCardSettlementAsync(tenantId, service => service.MatchTenderAsync(environment.CompanyId, batch.Id, tenderId, Actor));

        await using var verify = _fixture.CreateContext(tenantId);
        var history = await verify.CardSettlementMatches.OrderBy(match => match.Id).ToListAsync(CancellationToken.None);
        history.Should().HaveCount(2);
        history[0].IsActive.Should().BeFalse();
        history[0].UnmatchReason.Should().Be("Processor reversed the batch line");
        history[1].IsActive.Should().BeTrue();
    }

    [PostgreSqlFact]
    public async Task CardSettlement_IsInvisibleToOtherTenants()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var batch = await WithCardSettlementAsync(tenantId, service => service.ImportBatchAsync(
            new CardSettlementBatchImportRequest(environment.CompanyId, "PROC-PRIVATE", Today(), 10m, 0m), Actor));

        var otherTenant = $"pos-other-{Guid.NewGuid():N}";
        var listed = await WithCardSettlementAsync(otherTenant, service => service.ListBatchesAsync(environment.CompanyId));
        listed.Should().BeEmpty();
        var reconciliation = await WithCardSettlementAsync(otherTenant, service =>
            service.GetReconciliationAsync(environment.CompanyId, batch.Id));
        reconciliation.Should().BeNull();
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(int RegisterId, int ShiftId)> OpenTillAsync(string tenantId, TestEnvironment environment)
    {
        return await WithServiceAsync(tenantId, async service =>
        {
            var register = await service.CreateRegisterAsync(environment.CompanyId, environment.LocationId, "Card till", Actor);
            var shift = await service.OpenShiftAsync(
                new OpenPosShiftRequest(environment.CompanyId, register.Id, OpeningFloat: 0m), Actor);
            return (register.Id, shift.Id);
        });
    }

    private async Task<int> CardSaleTenderIdAsync(
        string tenantId,
        TestEnvironment environment,
        int registerId,
        int shiftId,
        int quantity,
        decimal price,
        string key)
    {
        var total = quantity * price;
        var sale = await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            new PostPosSaleRequest(
                environment.CompanyId,
                registerId,
                shiftId,
                key,
                [new PostPosSaleLine(environment.ItemId, quantity, price)],
                [new PostPosTender(PaymentMethod.Card, total)]),
            environment.MutationScope,
            Actor));
        return await TenderIdAsync(tenantId, sale.PosSaleId, PaymentMethod.Card);
    }

    private async Task<int> CashSaleTenderIdAsync(
        string tenantId,
        TestEnvironment environment,
        int registerId,
        int shiftId,
        string key)
    {
        var sale = await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            new PostPosSaleRequest(
                environment.CompanyId,
                registerId,
                shiftId,
                key,
                [new PostPosSaleLine(environment.ItemId, 1, 15m)],
                [new PostPosTender(PaymentMethod.Cash, 15m)]),
            environment.MutationScope,
            Actor));
        return await TenderIdAsync(tenantId, sale.PosSaleId, PaymentMethod.Cash);
    }

    private async Task<int> TenderIdAsync(string tenantId, int saleId, PaymentMethod method)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var tender = await context.PosSaleTenders.SingleAsync(
            candidate => candidate.PosSaleId == saleId && candidate.Method == method,
            CancellationToken.None);
        return tender.Id;
    }

    private async Task<T> WithCardSettlementAsync<T>(string tenantId, Func<CardSettlementService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        return await action(new CardSettlementService(
            context, unitOfWork, Microsoft.Extensions.Logging.Abstractions.NullLogger<CardSettlementService>.Instance));
    }

    [PostgreSqlFact]
    public async Task TillApproval_VarianceAboveThreshold_HoldsTheCloseAndBlocksSalesUntilApproved()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        await WithTillApprovalAsync(tenantId, service => service.SetVarianceThresholdAsync(
            environment.CompanyId, registerId, 2m, Actor));
        await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            Sale(environment, registerId, shiftId, quantity: 2, price: 15m, cash: 30m, card: 0m), environment.MutationScope, Actor));

        var held = await WithServiceAsync(tenantId, service => service.CloseShiftAsync(
            new ClosePosShiftRequest(environment.CompanyId, shiftId, CountedCash: 25m), Actor));

        held.ApprovalRequired.Should().BeTrue();
        held.OverShortJournalNumber.Should().BeNull("nothing posts until a reviewer approves");
        (await TrialBalanceAsync(tenantId, environment.CompanyId)).Rows.Should().NotContain(row => row.AccountCode == "9100");

        var sale = async () => await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            Sale(environment, registerId, shiftId, quantity: 1, price: 15m, cash: 15m, card: 0m, key: "pos-held"),
            environment.MutationScope, Actor));
        await sale.Should().ThrowAsync<FinancePostingValidationException>();

        var secondClose = async () => await WithServiceAsync(tenantId, service => service.CloseShiftAsync(
            new ClosePosShiftRequest(environment.CompanyId, shiftId, CountedCash: 25m), Actor));
        (await secondClose.Should().ThrowAsync<FinancePostingConflictException>()).WithMessage("*awaiting approval*");

        var approved = await WithTillApprovalAsync(tenantId, service => service.ApproveCloseAsync(
            environment.CompanyId, shiftId, "supervisor"));
        approved.Status.Should().Be(PosShiftStatus.Closed);
        approved.OverShortValue.Should().Be(-5m);
        approved.OverShortJournalNumber.Should().StartWith("JV");
        approved.ApprovedBy.Should().Be("supervisor");

        var trialBalance = await TrialBalanceAsync(tenantId, environment.CompanyId);
        trialBalance.Rows.Single(row => row.AccountCode == "9100").Debit.Should().Be(5m, "cash short expense");
        trialBalance.TotalDebit.Should().Be(trialBalance.TotalCredit);

        var again = async () => await WithTillApprovalAsync(tenantId, service => service.ApproveCloseAsync(
            environment.CompanyId, shiftId, "supervisor"));
        await again.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task TillApproval_RejectReturnsTheShiftToTheCashierWithAReason()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        await WithTillApprovalAsync(tenantId, service => service.SetVarianceThresholdAsync(
            environment.CompanyId, registerId, 2m, Actor));
        await WithServiceAsync(tenantId, service => service.CloseShiftAsync(
            new ClosePosShiftRequest(environment.CompanyId, shiftId, CountedCash: 25m), Actor));

        var noReason = async () => await WithTillApprovalAsync(tenantId, service => service.RejectCloseAsync(
            environment.CompanyId, shiftId, "   ", "supervisor"));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        var rejected = await WithTillApprovalAsync(tenantId, service => service.RejectCloseAsync(
            environment.CompanyId, shiftId, "Recount: a note was misplaced", "supervisor"));
        rejected.Status.Should().Be(PosShiftStatus.Open);
        rejected.RejectionReason.Should().Be("Recount: a note was misplaced");

        var sale = await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            Sale(environment, registerId, shiftId, quantity: 1, price: 15m, cash: 15m, card: 0m, key: "after-reject"),
            environment.MutationScope, Actor));
        sale.TotalValue.Should().Be(15m, "the shift transacts again after the rejection");

        var approve = async () => await WithTillApprovalAsync(tenantId, service => service.ApproveCloseAsync(
            environment.CompanyId, shiftId, "supervisor"));
        await approve.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task TillApproval_VarianceWithinThreshold_ClosesImmediately()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, shiftId) = await OpenTillAsync(tenantId, environment);
        await WithTillApprovalAsync(tenantId, service => service.SetVarianceThresholdAsync(
            environment.CompanyId, registerId, 10m, Actor));
        await WithServiceAsync(tenantId, service => service.PostSaleAsync(
            Sale(environment, registerId, shiftId, quantity: 2, price: 15m, cash: 30m, card: 0m), environment.MutationScope, Actor));

        var close = await WithServiceAsync(tenantId, service => service.CloseShiftAsync(
            new ClosePosShiftRequest(environment.CompanyId, shiftId, CountedCash: 25m), Actor));

        close.ApprovalRequired.Should().BeFalse();
        close.OverShortJournalNumber.Should().StartWith("JV");
    }

    [PostgreSqlFact]
    public async Task TillApproval_ThresholdMustBeNonNegative()
    {
        var tenantId = $"pos-{Guid.NewGuid():N}";
        var environment = await CreateEnvironmentAsync(tenantId);
        var (registerId, _) = await OpenTillAsync(tenantId, environment);

        var negative = async () => await WithTillApprovalAsync(tenantId, service => service.SetVarianceThresholdAsync(
            environment.CompanyId, registerId, -1m, Actor));

        await negative.Should().ThrowAsync<FinancePostingValidationException>();
    }

    private async Task<T> WithTillApprovalAsync<T>(string tenantId, Func<TillApprovalService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        // One unit of work must back both services, as it does under dependency injection, so the
        // posting joins the approval transaction instead of opening a second one.
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(
            context, unitOfWork, Microsoft.Extensions.Logging.Abstractions.NullLogger<FiscalCalendarService>.Instance);
        var journal = new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JournalPostingService>.Instance);
        return await action(new TillApprovalService(
            context,
            unitOfWork,
            new AccountPostingPolicyService(context),
            journal,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TillApprovalService>.Instance));
    }

    private static PostPosSaleRequest Sale(
        TestEnvironment environment, int registerId, int shiftId,
        int quantity, decimal price, decimal cash, decimal card, string key = "pos-key-1")
    {
        var tenders = new List<PostPosTender>();
        if (cash > 0)
        {
            tenders.Add(new PostPosTender(PaymentMethod.Cash, cash));
        }
        if (card > 0)
        {
            tenders.Add(new PostPosTender(PaymentMethod.BankTransfer, card));
        }

        return new PostPosSaleRequest(
            environment.CompanyId,
            registerId,
            shiftId,
            key,
            [new PostPosSaleLine(environment.ItemId, quantity, price)],
            tenders);
    }

    private static JournalPostingService BuildPostingService(InventoryDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        return new JournalPostingService(
            context, unitOfWork, fiscalCalendar, new DocumentNumberService(context, unitOfWork),
            NullLogger<JournalPostingService>.Instance);
    }

    private static PosService BuildService(InventoryDbContext context)
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
        return new PosService(
            context, unitOfWork, stockService,
            new JournalPostingService(
                context, unitOfWork, fiscalCalendar, documentNumbers, NullLogger<JournalPostingService>.Instance),
            new AccountPostingPolicyService(context),
            fiscalCalendar,
            documentNumbers,
            NullLogger<PosService>.Instance);
    }

    private async Task<T> WithServiceAsync<T>(string tenantId, Func<PosService, Task<T>> action)
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

    private async Task<TrialBalanceResult> TrialBalanceAsync(string tenantId, int companyId)
    {
        await using var context = _fixture.CreateContext(tenantId);
        return await BuildPostingService(context).GetTrialBalanceAsync(companyId, null, null);
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
                Code = "POS-MAIN", LegalName = "POS test company", BaseCurrency = "USD", IsActive = true
            };
            var branch = new Branch { Code = "POS-BR", Name = "Store", TimeZoneId = "UTC" };
            var item = new Item
            {
                ItemCode = $"POS-{Guid.NewGuid():N}"[..20], Description = "Retail item",
                Rate = 15m, IsActive = true
            };
            var location = new Location { Name = "Store floor" };
            branch.Company = company;
            location.Branch = branch;
            setup.AddRange(company, branch, item, location);
            await setup.SaveChangesAsAsync(Actor);
            companyId = company.Id;
            locationId = location.Id;
            itemId = item.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1020", Name = "Cash on Hand", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "3000", Name = "Opening Equity", AccountType = "Equity", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5000", Name = "Cost of Goods Sold", AccountType = "Expense", IsGroupAccount = false, IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "9100", Name = "Cash Over/Short", AccountType = "Expense", IsGroupAccount = false, IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);
            ChartOfAccount ByCode(string code) => accounts.Single(account => account.AccountCode == code);
            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = ByCode("1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CashOnHand, ChartOfAccountId = ByCode("1020").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = ByCode("1300").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.SalesRevenue, ChartOfAccountId = ByCode("4000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CostOfGoodsSold, ChartOfAccountId = ByCode("5000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.CashOverShort, ChartOfAccountId = ByCode("9100").Id, IsActive = true });
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

        // Opening valued stock: 60 units at cost 10, journalized as an opening balance.
        var stockContext = _fixture.CreateContext(tenantId);
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
                itemId, locationId, 60, notes: "opening stock",
                unitCost: CostPerUnit,
                mutationScope: new StockMutationScope(companyId, () => Task.FromResult(true)));
        }
        finally
        {
            await stockContext.DisposeAsync();
        }

        await using (var journalContext = _fixture.CreateContext(tenantId))
        {
            var inventoryAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "1300");
            var equityAccount = await journalContext.ChartOfAccounts.SingleAsync(
                account => account.CompanyId == companyId && account.AccountCode == "3000");
            await BuildPostingService(journalContext).PostAsync(
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
                        new JournalLineInput(inventoryAccount.Id, "Opening stock", 600m, 0m),
                        new JournalLineInput(equityAccount.Id, "Opening equity", 0m, 600m)
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

    private sealed class TestEnvironment
    {
        public int CompanyId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public StockMutationScope MutationScope { get; init; }
    }
}
