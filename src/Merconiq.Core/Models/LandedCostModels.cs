using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

public sealed record LandedCostChargeInput(
    int ExpenseAccountId,
    string Description,
    decimal Amount);

public sealed record CreateLandedCostVoucherRequest(
    int CompanyId,
    DateOnly PostingDate,
    IReadOnlyList<int> GoodsReceiptIds,
    IReadOnlyList<LandedCostChargeInput> Charges,
    string? Notes = null);

public sealed record LandedCostItemSummary(
    int GoodsReceiptLineId,
    int ItemId,
    int LocationId,
    decimal InitialCost,
    decimal AdditionalCharge,
    decimal FinalValuationCost);

public sealed record LandedCostVoucherResult(
    int LandedCostVoucherId,
    string VoucherNumber,
    LandedCostStatus Status,
    decimal TotalAdditionalCharges,
    string? JournalNumber,
    IReadOnlyList<LandedCostItemSummary> ItemAllocations);
