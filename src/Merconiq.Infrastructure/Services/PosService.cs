using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Point-of-sale operation. Checkouts issue valued stock through the stock ledger, split tenders
/// into cash/bank debit journals against revenue, and book the exact issued value as COGS — all
/// atomically, once per request key, and only into an open shift. Refunds are bounded by the
/// sold-minus-refunded balance per line. Shift close reconciles counted cash against the
/// theoretical drawer and posts any variance to the cash over/short account once.
/// </summary>
public sealed class PosService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IStockService stockService,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    IDocumentNumberService documentNumbers,
    ILogger<PosService> logger) : IPosService
{
    public const string SaleRequestKeyIndex = "UX_PosSales_RequestKey";
    public const string RefundRequestKeyIndex = "UX_PosRefunds_RequestKey";

    public const string SaleDocumentType = "PosSale";
    public const string RefundDocumentType = "PosRefund";
    public const string ShiftDocumentType = "PosShift";

    private const int MaximumLines = 200;

    /// <inheritdoc />
    public async Task<PosRegister> CreateRegisterAsync(
        int companyId, int locationId, string name, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var registerName = RequireBounded(name, "Register name", 100);
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-register:{companyId}", cancellationToken);
            if (!await context.Companies.AnyAsync(c => c.Id == companyId && c.IsActive, cancellationToken))
                throw new FinancePostingValidationException("Active company not found.");
            if (await context.PosRegisters.AnyAsync(
                    register => register.CompanyId == companyId && register.Name == registerName, cancellationToken))
                throw new FinancePostingConflictException(
                    $"A register named '{registerName}' already exists in this company.");
            if (!await context.Locations.AnyAsync(location => location.Id == locationId, cancellationToken))
                throw new FinancePostingValidationException("Location not found.");

            context.PosRegisters.Add(new PosRegister
            {
                CompanyId = companyId, LocationId = locationId, Name = registerName, IsActive = true
            });
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return await context.PosRegisters.SingleAsync(
            register => register.CompanyId == companyId && register.Name == registerName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PosShift> OpenShiftAsync(
        OpenPosShiftRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.OpeningFloat < 0)
            throw new FinancePostingValidationException("The opening float must not be negative.");

        PosShift? shift = null;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-shift:{request.CompanyId}", cancellationToken);
            var register = await context.PosRegisters.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.RegisterId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Register not found in this company.");
            if (!register.IsActive)
                throw new FinancePostingValidationException("Inactive registers cannot open shifts.");

            shift = new PosShift
            {
                CompanyId = request.CompanyId,
                RegisterId = register.Id,
                Status = PosShiftStatus.Open,
                OpeningFloat = request.OpeningFloat,
                OpenedAtUtc = DateTime.UtcNow,
                OpenedBy = actor.Trim()
            };
            context.PosShifts.Add(shift);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);

        logger.LogInformation("POS shift {ShiftId} opened on register {RegisterId} with float {Float}",
            shift!.Id, request.RegisterId, request.OpeningFloat);
        return shift;
    }

    /// <inheritdoc />
    public async Task<PosSaleResult> PostSaleAsync(
        PostPosSaleRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateSaleRequest(request);
        var normalized = request with
        {
            RequestKey = request.RequestKey.Trim(),
            Lines = request.Lines.Select(line => line with
            {
                UnitPrice = decimal.Round(line.UnitPrice, 6, MidpointRounding.AwayFromZero)
            }).ToArray()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("pos-sale\u001f").Append(request.CompanyId).Append('\u001f').Append(request.RegisterId)
                .Append('\u001f').Append(request.ShiftId).Append('\u001f').Append(normalized.RequestKey)
                .Append('\u001f').Append(request.CustomerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('\u001f');
            foreach (var line in normalized.Lines)
            {
                h.Append(line.ItemId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append(line.UnitPrice.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f');
            }
            foreach (var tender in normalized.Tenders)
            {
                h.Append((int)tender.Method).Append('\u001f')
                    .Append(tender.Amount.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f');
            }
        });

        PosSaleResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync($"pos-sale:{normalized.CompanyId}", cancellationToken);
                var committed = await context.PosSales
                    .Include(sale => sale.Lines)
                    .SingleOrDefaultAsync(sale => sale.CompanyId == normalized.CompanyId &&
                        sale.RequestKey == normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This checkout request key was already posted with a different payload.");
                    var committedCogs = await CostOfGoodsForSaleAsync(committed, cancellationToken);
                    result = new PosSaleResult(committed.Id, null, committed.TotalValue, committedCogs, Replayed: true);
                    return;
                }

                result = await SaleCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.PosSales.AnyAsync(
                sale => sale.CompanyId == normalized.CompanyId && sale.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<PosRefundResult> PostRefundAsync(
        PostPosRefundRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateRefundRequest(request);
        var normalized = request with { RequestKey = request.RequestKey.Trim(), Reason = request.Reason.Trim() };
        var requestHash = ComputeHash(h =>
        {
            h.Append("pos-refund\u001f").Append(request.CompanyId).Append('\u001f').Append(request.OriginalPosSaleId)
                .Append('\u001f').Append(normalized.RequestKey).Append('\u001f').Append(normalized.Reason)
                .Append('\u001f');
            foreach (var line in normalized.Lines)
            {
                h.Append(line.PosSaleLineId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append((int)line.Disposition).Append('\u001f');
            }
        });

        PosRefundResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync($"pos-sale:{normalized.CompanyId}", cancellationToken);
                var committed = await context.PosRefunds
                    .Include(refund => refund.Lines)
                    .SingleOrDefaultAsync(refund => refund.CompanyId == normalized.CompanyId &&
                        refund.RequestKey == normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This refund request key was already posted with a different payload.");
                    result = new PosRefundResult(committed.Id, committed.TotalValue,
                        committed.Lines.Where(line => line.StockTransactionId is not null)
                            .Sum(line => line.RefundValue),
                        Replayed: true);
                    return;
                }

                result = await RefundCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.PosRefunds.AnyAsync(
                refund => refund.CompanyId == normalized.CompanyId && refund.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<PosShiftCloseResult> CloseShiftAsync(
        ClosePosShiftRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        PosShiftCloseResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-shift:{request.CompanyId}", cancellationToken);
            var shift = await context.PosShifts
                .Include(candidate => candidate.Register)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ShiftId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Shift not found in this company.");
            if (shift.Status == PosShiftStatus.Closed)
                throw new FinancePostingConflictException("The shift is already closed.");

            var saleIds = await context.PosSales
                .Where(sale => sale.ShiftId == shift.Id)
                .Select(sale => sale.Id)
                .ToListAsync(cancellationToken);
            var cashSales = await context.PosSaleTenders
                .Where(tender => saleIds.Contains(tender.PosSaleId) && tender.Method == PaymentMethod.Cash)
                .SumAsync(tender => (decimal?)tender.Amount, cancellationToken) ?? 0m;
            var refundIds = await context.PosRefunds
                .Where(refund => refund.ShiftId == shift.Id)
                .Select(refund => refund.Id)
                .ToListAsync(cancellationToken);
            // Refunds settle in cash by design; their value reduces the expected drawer.
            var cashRefunds = await context.PosRefunds
                .Where(refund => refundIds.Contains(refund.Id))
                .SumAsync(refund => (decimal?)refund.TotalValue, cancellationToken) ?? 0m;

            var expectedCash = shift.OpeningFloat + cashSales - cashRefunds;
            var overShort = decimal.Round(request.CountedCash - expectedCash, 6, MidpointRounding.AwayFromZero);

            string? overShortJournal = null;
            if (overShort != 0)
            {
                var cashAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.CashOnHand, cancellationToken);
                var overShortAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.CashOverShort, cancellationToken);
                var posted = await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        request.CompanyId,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        (await RequireCompanyAsync(request.CompanyId, cancellationToken)).BaseCurrency,
                        $"Cash over/short for shift on register {shift.Register.Name}",
                        ShiftDocumentType,
                        shift.Id.ToString(CultureInfo.InvariantCulture),
                        "Close",
                        1,
                        BuildOverShortLines(overShortAccount.ChartOfAccountId, cashAccount.ChartOfAccountId, overShort)),
                    actor,
                    cancellationToken);
                overShortJournal = posted.JournalNumber;
            }

            shift.Status = PosShiftStatus.Closed;
            shift.ClosedAtUtc = DateTime.UtcNow;
            shift.ClosedBy = actor.Trim();
            shift.CountedCash = request.CountedCash;
            shift.ExpectedCash = expectedCash;
            shift.OverShortValue = overShort;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

            logger.LogInformation(
                "POS shift {ShiftId} closed: expected {Expected}, counted {Counted}, over/short {OverShort}",
                shift.Id, expectedCash, request.CountedCash, overShort);
            result = new PosShiftCloseResult(
                shift.Id, shift.OpeningFloat, cashSales, cashRefunds, expectedCash, request.CountedCash,
                overShort, overShortJournal);
        }, cancellationToken: cancellationToken);
        return result;
    }

    private async Task<PosSaleResult> SaleCoreAsync(
        PostPosSaleRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var register = await context.PosRegisters
            .Include(candidate => candidate.Location)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.RegisterId && candidate.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Register not found in this company.");
        var shift = await context.PosShifts.SingleOrDefaultAsync(
                candidate => candidate.Id == request.ShiftId &&
                    candidate.CompanyId == request.CompanyId &&
                    candidate.RegisterId == register.Id,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Shift not found for this register.");
        if (shift.Status != PosShiftStatus.Open)
            throw new FinancePostingValidationException("A closed shift cannot transact; open a new shift.");

        var postingDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToList();
        var items = await context.Items
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var sale = new PosSale
        {
            CompanyId = request.CompanyId,
            RegisterId = register.Id,
            ShiftId = shift.Id,
            CustomerId = request.CustomerId,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostingDate = postingDate,
            SoldAtUtc = DateTime.UtcNow,
            SoldBy = actor.Trim()
        };
        var total = 0m;
        var knownTransactionIds = context.StockTransactions.Local.Select(t => t.Id).ToHashSet();
        foreach (var line in request.Lines)
        {
            if (!items.ContainsKey(line.ItemId))
                throw new FinancePostingValidationException($"Item {line.ItemId} does not exist.");

            await stockService.SellStockAsync(
                line.ItemId,
                register.LocationId,
                line.Quantity,
                notes: $"POS sale at {register.Name}",
                mutationScope: mutationScope);
            var issued = context.StockTransactions.Local
                .Where(t => !knownTransactionIds.Contains(t.Id) &&
                    t.ItemId == line.ItemId &&
                    t.FromLocationId == register.LocationId &&
                    t.Quantity == line.Quantity)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault()
                ?? throw new FinancePostingValidationException(
                    "The stock ledger did not record the sold movement; checkout aborted.");
            knownTransactionIds.Add(issued.Id);

            sale.Lines.Add(new PosSaleLine
            {
                CompanyId = request.CompanyId,
                LineNumber = sale.Lines.Count + 1,
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineValue = decimal.Round(line.Quantity * line.UnitPrice, 6, MidpointRounding.AwayFromZero),
                StockTransactionId = issued.Id
            });
            total += sale.Lines.Last().LineValue;
        }

        var tenderTotal = request.Tenders.Sum(tender => tender.Amount);
        if (tenderTotal != total)
            throw new FinancePostingValidationException(
                $"Tenders total {tenderTotal} must equal the sale total {total}.");
        sale.TotalValue = total;
        foreach (var tender in request.Tenders)
        {
            sale.Tenders.Add(new PosSaleTender
            {
                CompanyId = request.CompanyId,
                LineNumber = sale.Tenders.Count + 1,
                Method = tender.Method,
                Amount = tender.Amount
            });
        }

        context.PosSales.Add(sale);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        // Revenue journal: Dr Cash/Bank per tender / Cr Sales revenue for the total.
        var revenueAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.SalesRevenue, cancellationToken);
        var cashAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.CashOnHand, cancellationToken);
        var bankAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.Bank, cancellationToken);
        var debitLines = request.Tenders
            .GroupBy(tender => tender.Method)
            .Select(group => new JournalLineInput(
                group.Key == PaymentMethod.Cash ? cashAccount.ChartOfAccountId : bankAccount.ChartOfAccountId,
                group.Key == PaymentMethod.Cash ? "Cash sale" : "Card/bank sale",
                group.Sum(tender => tender.Amount),
                0m))
            .ToList();
        debitLines.Add(new JournalLineInput(revenueAccount.ChartOfAccountId, "POS revenue", 0m, total));
        var revenueJournal = await journalPosting.PostAsync(
            new JournalPostingRequest(
                request.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"POS sale at {register.Name}",
                SaleDocumentType,
                sale.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                debitLines),
            actor,
            cancellationToken);

        // COGS journal: exact issued moving-average value.
        var cogs = await PostCogsJournalAsync(sale, company, postingDate, actor, cancellationToken);

        logger.LogInformation(
            "POS sale {SaleId} posted on register {RegisterId}: total {Total}, COGS {Cogs}",
            sale.Id, register.Id, total, cogs);
        return new PosSaleResult(sale.Id, null, total, cogs, Replayed: false);
    }

    private async Task<decimal> PostCogsJournalAsync(
        PosSale sale,
        Company company,
        DateOnly postingDate,
        string actor,
        CancellationToken cancellationToken)
    {
        var cogsByLine = await context.StockValuationEntries
            .Where(entry => sale.Lines.Select(line => line.StockTransactionId).Contains(entry.StockTransactionId))
            .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
        var cogsTotal = sale.Lines.Sum(line => cogsByLine.GetValueOrDefault(line.StockTransactionId));
        if (cogsTotal <= 0)
        {
            return 0m;
        }

        var cogsAccount = await postingPolicy.ResolveAsync(
            sale.CompanyId, AccountPostingRole.CostOfGoodsSold, cancellationToken);
        var inventoryAccount = await postingPolicy.ResolveAsync(
            sale.CompanyId, AccountPostingRole.Inventory, cancellationToken);
        await journalPosting.PostAsync(
            new JournalPostingRequest(
                sale.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"Cost of goods sold for POS sale",
                SaleDocumentType,
                $"{sale.Id}-cogs",
                "Post",
                1,
                [
                    new JournalLineInput(cogsAccount.ChartOfAccountId, "POS cost of goods", cogsTotal, 0m),
                    new JournalLineInput(inventoryAccount.ChartOfAccountId, "Stock issued", 0m, cogsTotal)
                ]),
            actor,
            cancellationToken);
        return cogsTotal;
    }

    private async Task<decimal> CostOfGoodsForSaleAsync(PosSale sale, CancellationToken cancellationToken)
    {
        var cogsByLine = await context.StockValuationEntries
            .Where(entry => sale.Lines.Select(line => line.StockTransactionId).Contains(entry.StockTransactionId))
            .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
        return sale.Lines.Sum(line => cogsByLine.GetValueOrDefault(line.StockTransactionId));
    }

    private async Task<PosRefundResult> RefundCoreAsync(
        PostPosRefundRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var original = await context.PosSales
            .Include(sale => sale.Lines)
            .SingleOrDefaultAsync(
                sale => sale.Id == request.OriginalPosSaleId && sale.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Original POS sale not found in this company.");
        var shift = await context.PosShifts.SingleOrDefaultAsync(
                candidate => candidate.Id == request.ShiftId && candidate.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Shift not found for this refund.");
        if (shift.Status != PosShiftStatus.Open)
            throw new FinancePostingValidationException("A closed shift cannot transact; open a new shift.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        // Refunds are bounded by sold minus already-refunded per line; double refunds cannot pass.
        var saleLineIds = request.Lines.Select(line => line.PosSaleLineId).ToList();
        var soldLines = original.Lines.ToDictionary(line => line.Id);
        var refunded = await context.PosRefundLines
            .Where(line => saleLineIds.Contains(line.PosSaleLineId))
            .GroupBy(line => line.PosSaleLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.LineId, row => row.Quantity, cancellationToken);

        var refund = new PosRefund
        {
            CompanyId = request.CompanyId,
            RegisterId = original.RegisterId,
            ShiftId = shift.Id,
            OriginalPosSaleId = original.Id,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostingDate = postingDate,
            RefundedAtUtc = DateTime.UtcNow,
            RefundedBy = actor.Trim(),
            Reason = request.Reason
        };
        var refundTotal = 0m;
        foreach (var line in request.Lines)
        {
            if (!soldLines.TryGetValue(line.PosSaleLineId, out var soldLine))
                throw new FinancePostingValidationException(
                    $"Sale line {line.PosSaleLineId} does not belong to the original sale.");
            refunded.TryGetValue(line.PosSaleLineId, out var priorRefunded);
            if (priorRefunded + line.Quantity > soldLine.Quantity)
                throw new FinancePostingValidationException(
                    $"Refunding {line.Quantity} of sale line {line.PosSaleLineId} exceeds the " +
                    $"refundable balance of {soldLine.Quantity - priorRefunded}.");

            int? returnTransactionId = null;
            if (line.Disposition == StockReturnDisposition.Restockable)
            {
                var knownValuationIds = context.StockValuationEntries.Local.Select(entry => entry.Id).ToHashSet();
                var knownReturnIds = context.StockTransactions.Local.Select(t => t.Id).ToHashSet();
                await stockService.ReturnStockAsync(
                    new CreateStockReturnRequest(
                        soldLine.StockTransactionId,
                        line.Quantity,
                        line.Disposition,
                        $"PosRefund:{request.RequestKey}:{line.PosSaleLineId}",
                        Notes: $"POS refund: {request.Reason}"),
                    mutationScope,
                    cancellationToken);
                var returnEntry = context.StockValuationEntries.Local
                    .Where(entry => !knownValuationIds.Contains(entry.Id))
                    .OrderByDescending(entry => entry.Id)
                    .FirstOrDefault();
                returnTransactionId = context.StockTransactions.Local
                    .Where(t => !knownReturnIds.Contains(t.Id))
                    .OrderByDescending(t => t.Id)
                    .FirstOrDefault()?.Id;
                if (returnEntry is null || returnEntry.TotalValue <= 0)
                    throw new FinancePostingValidationException(
                        "The stock ledger did not record the returned value; refund aborted.");
            }

            var refundValue = decimal.Round(line.Quantity * soldLine.UnitPrice, 6, MidpointRounding.AwayFromZero);
            refund.Lines.Add(new PosRefundLine
            {
                CompanyId = request.CompanyId,
                LineNumber = refund.Lines.Count + 1,
                PosSaleLineId = soldLine.Id,
                Quantity = line.Quantity,
                StockTransactionId = returnTransactionId,
                Disposition = line.Disposition,
                RefundValue = refundValue
            });
            refundTotal += refundValue;
        }

        refund.TotalValue = refundTotal;
        context.PosRefunds.Add(refund);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        // Refund journals: money back to the customer (Dr Sales / Cr Cash) and, for restocked
        // lines, the returned value back into inventory (Dr Inventory / Cr COGS).
        var revenueAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.SalesRevenue, cancellationToken);
        var cashAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.CashOnHand, cancellationToken);
        await journalPosting.PostAsync(
            new JournalPostingRequest(
                request.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"POS refund: {request.Reason}",
                RefundDocumentType,
                refund.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                [
                    new JournalLineInput(revenueAccount.ChartOfAccountId, "Sales reversal", refundTotal, 0m),
                    new JournalLineInput(cashAccount.ChartOfAccountId, "Cash refund", 0m, refundTotal)
                ]),
            actor,
            cancellationToken);

        var restockedValue = 0m;
        var restockLines = refund.Lines
            .Where(line => line.StockTransactionId is not null)
            .Select(line => line.StockTransactionId!.Value)
            .ToList();
        if (restockLines.Count > 0)
        {
            var values = await context.StockValuationEntries
                .Where(entry => restockLines.Contains(entry.StockTransactionId))
                .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
            restockedValue = restockLines.Sum(id => values.GetValueOrDefault(id));
            if (restockedValue > 0)
            {
                var inventoryAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.Inventory, cancellationToken);
                var cogsAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.CostOfGoodsSold, cancellationToken);
                await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        request.CompanyId,
                        postingDate,
                        company.BaseCurrency,
                        $"Stock returned to inventory for POS refund",
                        RefundDocumentType,
                        $"{refund.Id}-restock",
                        "Post",
                        1,
                        [
                            new JournalLineInput(inventoryAccount.ChartOfAccountId, "Stock returned", restockedValue, 0m),
                            new JournalLineInput(cogsAccount.ChartOfAccountId, "COGS reversal", 0m, restockedValue)
                        ]),
                    actor,
                    cancellationToken);
            }
        }

        logger.LogInformation(
            "POS refund {RefundId} posted against sale {SaleId}: total {Total}, restocked {Restocked}",
            refund.Id, original.Id, refundTotal, restockedValue);
        return new PosRefundResult(refund.Id, refundTotal, restockedValue, Replayed: false);
    }

    private static JournalLineInput[] BuildOverShortLines(
        int overShortAccountId, int cashAccountId, decimal overShort) =>
        overShort < 0
            ?
            [
                new JournalLineInput(overShortAccountId, "Cash short", -overShort, 0m),
                new JournalLineInput(cashAccountId, "Cash counted below expected", 0m, -overShort)
            ]
            :
            [
                new JournalLineInput(cashAccountId, "Cash counted above expected", overShort, 0m),
                new JournalLineInput(overShortAccountId, "Cash over", 0m, overShort)
            ];

    private Task<Company> RequireCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        context.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private static void ValidateSaleRequest(PostPosSaleRequest request)
    {
        if (request.CompanyId <= 0 || request.RegisterId <= 0 || request.ShiftId <= 0)
            throw new FinancePostingValidationException(
                "A positive CompanyId, RegisterId and ShiftId are required.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A checkout request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A checkout requires between 1 and {MaximumLines} lines.");
        if (request.Tenders is null || request.Tenders.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException("A checkout requires at least one tender.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.ItemId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0)
                throw new FinancePostingValidationException(
                    "Every sale line needs a positive item id and quantity, and a non-negative price.");
        }
        foreach (var tender in request.Tenders)
        {
            if (tender is null || !Enum.IsDefined(tender.Method) || tender.Amount <= 0)
                throw new FinancePostingValidationException("Every tender needs a defined method and positive amount.");
        }
    }

    private static void ValidateRefundRequest(PostPosRefundRequest request)
    {
        if (request.CompanyId <= 0 || request.RegisterId <= 0 || request.ShiftId <= 0 || request.OriginalPosSaleId <= 0)
            throw new FinancePostingValidationException(
                "A positive CompanyId, RegisterId, ShiftId and OriginalPosSaleId are required.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A refund request key of 1 to 200 characters is required.");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
            throw new FinancePostingValidationException("A refund reason of 1 to 500 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A refund requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.PosSaleLineId <= 0 || line.Quantity <= 0 || !Enum.IsDefined(line.Disposition))
                throw new FinancePostingValidationException(
                    "Every refund line needs a positive sale-line id, quantity and defined disposition.");
        }
    }

    private static string RequireBounded(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new FinancePostingValidationException($"{field} of 1 to {maxLength} characters is required.");
        return trimmed;
    }

    private static string ComputeHash(Action<StringBuilder> build)
    {
        var canonical = new StringBuilder();
        build(canonical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
