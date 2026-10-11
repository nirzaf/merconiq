using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One checkout line; price per base unit is snapshotted at posting.</summary>
public sealed record PostPosSaleLine(int ItemId, int Quantity, decimal UnitPrice);

/// <summary>One tender; the sum of tenders must equal the sale total.</summary>
public sealed record PostPosTender(PaymentMethod Method, decimal Amount);

/// <summary>Posts one checkout on an open shift; idempotent per request key.</summary>
public sealed record PostPosSaleRequest(
    int CompanyId,
    int RegisterId,
    int ShiftId,
    string RequestKey,
    IReadOnlyList<PostPosSaleLine> Lines,
    IReadOnlyList<PostPosTender> Tenders,
    int? CustomerId = null);

/// <summary>The committed-or-replayed checkout outcome.</summary>
public sealed record PosSaleResult(
    int PosSaleId,
    string? SaleNumber,
    decimal TotalValue,
    decimal CostOfGoodsSold,
    bool Replayed);

/// <summary>One refunded line against the original sale.</summary>
public sealed record PostPosRefundLine(int PosSaleLineId, int Quantity, StockReturnDisposition Disposition);

/// <summary>Posts one refund against a prior checkout; idempotent per request key.</summary>
public sealed record PostPosRefundRequest(
    int CompanyId,
    int RegisterId,
    int ShiftId,
    int OriginalPosSaleId,
    string RequestKey,
    string Reason,
    IReadOnlyList<PostPosRefundLine> Lines,
    DateOnly? PostingDate = null);

/// <summary>The committed-or-replayed refund outcome.</summary>
public sealed record PosRefundResult(
    int PosRefundId,
    decimal TotalValue,
    decimal RestockedValue,
    bool Replayed);

/// <summary>Opens one cashier shift with its float.</summary>
public sealed record OpenPosShiftRequest(int CompanyId, int RegisterId, decimal OpeningFloat);

/// <summary>Closes one open shift with the physically counted cash.</summary>
public sealed record ClosePosShiftRequest(int CompanyId, int ShiftId, decimal CountedCash);

/// <summary>The closed-shift outcome with the derived cash reconciliation.</summary>
public sealed record PosShiftCloseResult(
    int PosShiftId,
    decimal OpeningFloat,
    decimal CashSales,
    decimal CashRefunds,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal OverShortValue,
    string? OverShortJournalNumber,
    bool ApprovalRequired = false);
