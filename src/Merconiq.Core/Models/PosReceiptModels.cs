namespace Merconiq.Core.Models;

/// <summary>
/// Committed checkout presented as a customer receipt. Built only from persisted sale rows, so a
/// reprint shows exactly what was sold, tendered and recorded.
/// </summary>
public sealed record PosReceipt(
    int CompanyId,
    int SaleId,
    string CompanyName,
    string? TradingName,
    string RegisterName,
    int ShiftId,
    DateTime SoldAtUtc,
    string SoldBy,
    int CurrencyScale,
    decimal Total,
    IReadOnlyList<PosReceiptLine> Lines,
    IReadOnlyList<PosReceiptTender> Tenders);

/// <summary>One sold line on a receipt; the item code and description are read from the item master.</summary>
public sealed record PosReceiptLine(
    string ItemCode,
    string ItemDescription,
    int Quantity,
    decimal UnitPrice,
    decimal LineValue);

/// <summary>One tender applied to the sale, with its human-readable payment method.</summary>
public sealed record PosReceiptTender(string MethodName, decimal Amount);
