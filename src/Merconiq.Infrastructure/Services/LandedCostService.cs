using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class LandedCostService(
    InventoryDbContext context,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy accountPolicy,
    ILogger<LandedCostService> logger) : ILandedCostService
{
    public async Task<LandedCostVoucher> CreateVoucherAsync(
        CreateLandedCostVoucherRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (request.GoodsReceiptIds.Count == 0)
            throw new FinancePostingValidationException("At least one GoodsReceipt is required.");
        if (request.Charges.Count == 0)
            throw new FinancePostingValidationException("At least one landed cost charge is required.");

        var totalCharges = request.Charges.Sum(c => c.Amount);
        if (totalCharges <= 0)
            throw new FinancePostingValidationException("Total additional charges must be positive.");

        var voucherNumber = $"LCV-{request.CompanyId}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var voucher = new LandedCostVoucher
        {
            CompanyId = request.CompanyId,
            VoucherNumber = voucherNumber,
            PostingDate = request.PostingDate,
            Status = LandedCostStatus.Draft,
            TotalAdditionalCharges = totalCharges,
            Notes = request.Notes
        };

        foreach (var grId in request.GoodsReceiptIds.Distinct())
        {
            voucher.Receipts.Add(new LandedCostReceipt
            {
                GoodsReceiptId = grId
            });
        }

        foreach (var charge in request.Charges)
        {
            voucher.Charges.Add(new LandedCostCharge
            {
                ExpenseAccountId = charge.ExpenseAccountId,
                Description = charge.Description,
                Amount = charge.Amount
            });
        }

        context.LandedCostVouchers.Add(voucher);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Created LandedCostVoucher {VoucherNumber} with charges {Total}", voucher.VoucherNumber, totalCharges);
        return voucher;
    }

    public async Task<LandedCostVoucherResult> PostLandedCostVoucherAsync(
        int voucherId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (voucherId <= 0)
            throw new FinancePostingValidationException("A valid LandedCostVoucherId is required.");

        var voucher = await context.LandedCostVouchers
            .Include(v => v.Receipts)
            .Include(v => v.Charges)
            .Include(v => v.ItemAllocations)
            .FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken)
            ?? throw new FinancePostingValidationException("LandedCostVoucher not found.");

        if (voucher.Status != LandedCostStatus.Draft)
            throw new FinancePostingValidationException($"Voucher is already {voucher.Status}. Only Draft vouchers can be posted.");

        var receiptIds = voucher.Receipts.Select(r => r.GoodsReceiptId).ToList();
        var receipts = await context.GoodsReceipts
            .Include(gr => gr.Lines)
            .Where(gr => receiptIds.Contains(gr.Id) && gr.CompanyId == voucher.CompanyId)
            .ToListAsync(cancellationToken);

        if (receipts.Count == 0)
            throw new FinancePostingValidationException("No valid goods receipts found for this voucher.");

        var receiptLines = receipts.SelectMany(gr => gr.Lines).Where(l => l.AcceptedQuantity > 0).ToList();
        if (receiptLines.Count == 0)
            throw new FinancePostingValidationException("No accepted lines found in the linked goods receipts.");

        decimal totalReceiptValue = receiptLines.Sum(l => l.AcceptedQuantity * l.UnitCost);
        decimal totalCharges = voucher.Charges.Sum(c => c.Amount);

        var itemSummaries = new List<LandedCostItemSummary>();
        decimal allocatedSum = 0m;

        for (int i = 0; i < receiptLines.Count; i++)
        {
            var line = receiptLines[i];
            var lineValue = line.AcceptedQuantity * line.UnitCost;
            decimal allocatedCharge;

            if (i == receiptLines.Count - 1)
            {
                allocatedCharge = totalCharges - allocatedSum; // Ensure exact rounding allocation
            }
            else
            {
                var proportion = totalReceiptValue > 0 ? (lineValue / totalReceiptValue) : (1m / receiptLines.Count);
                allocatedCharge = Math.Round(totalCharges * proportion, 4);
                allocatedSum += allocatedCharge;
            }

            var parentReceipt = receipts.First(gr => gr.Lines.Any(l => l.Id == line.Id));
            var allocation = new LandedCostItemAllocation
            {
                LandedCostVoucherId = voucher.Id,
                GoodsReceiptLineId = line.Id,
                ItemId = line.ItemId,
                LocationId = parentReceipt.LocationId,
                ApplicableCharges = allocatedCharge
            };
            voucher.ItemAllocations.Add(allocation);

            // Update StockValuationBucket to increase value without altering quantity
            var bucket = await context.StockValuationBuckets
                .FirstOrDefaultAsync(b => b.ItemId == line.ItemId && b.LocationId == parentReceipt.LocationId, cancellationToken);
            if (bucket != null)
            {
                bucket.Value += allocatedCharge;
            }

            var initialUnitCost = line.UnitCost;
            var finalUnitCost = line.AcceptedQuantity > 0
                ? Math.Round((lineValue + allocatedCharge) / line.AcceptedQuantity, 4)
                : initialUnitCost;

            itemSummaries.Add(new LandedCostItemSummary(
                line.Id,
                line.ItemId,
                parentReceipt.LocationId,
                initialUnitCost,
                allocatedCharge,
                finalUnitCost));
        }

        // Post Journal Entry: Dr Inventory Asset / Cr Expense Account(s)
        var company = await context.Companies.FindAsync([voucher.CompanyId], cancellationToken)
            ?? throw new FinancePostingValidationException("Company not found.");
        var inventoryAccount = await accountPolicy.ResolveAsync(
            voucher.CompanyId, AccountPostingRole.Inventory, cancellationToken);
        var inventoryAccountId = inventoryAccount.ChartOfAccountId;

        var journalLines = new List<JournalLineInput>
        {
            new(inventoryAccountId, $"Landed cost capitalization: {voucher.VoucherNumber}", totalCharges, 0m)
        };

        foreach (var charge in voucher.Charges)
        {
            journalLines.Add(new(charge.ExpenseAccountId, $"Landed cost clearing: {charge.Description}", 0m, charge.Amount));
        }

        var journalResult = await journalPosting.PostAsync(
            new JournalPostingRequest(
                voucher.CompanyId,
                voucher.PostingDate,
                company.BaseCurrency,
                $"Landed Cost Voucher {voucher.VoucherNumber}",
                "LandedCostVoucher",
                voucher.VoucherNumber,
                "Post",
                1,
                journalLines),
            actor,
            cancellationToken);

        voucher.Status = LandedCostStatus.Submitted;
        voucher.JournalEntryId = journalResult.JournalEntryId;
        voucher.JournalNumber = journalResult.JournalNumber;

        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Posted LandedCostVoucher {VoucherNumber} with JV {JV}", voucher.VoucherNumber, journalResult.JournalNumber);

        return new LandedCostVoucherResult(
            voucher.Id,
            voucher.VoucherNumber,
            voucher.Status,
            voucher.TotalAdditionalCharges,
            voucher.JournalNumber,
            itemSummaries);
    }
}
