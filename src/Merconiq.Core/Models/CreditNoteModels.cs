using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One returned line against a posted goods-receipt line.</summary>
public sealed record PostPurchaseCreditNoteLine(int GoodsReceiptLineId, int Quantity);

/// <summary>Posts one supplier credit note; idempotent per request key.</summary>
public sealed record PostPurchaseCreditNoteRequest(
    int CompanyId,
    int SupplierId,
    string CreditNoteNumber,
    string Reason,
    string RequestKey,
    IReadOnlyList<PostPurchaseCreditNoteLine> Lines,
    DateOnly? PostingDate = null,
    string? Notes = null);

/// <summary>One returned line against a posted delivery line.</summary>
public sealed record PostSalesCreditNoteLine(
    int DeliveryNoteLineId,
    int Quantity,
    StockReturnDisposition Disposition = StockReturnDisposition.Restockable);

/// <summary>Posts one sales credit note; idempotent per request key.</summary>
public sealed record PostSalesCreditNoteRequest(
    int CompanyId,
    int CustomerId,
    string CreditNoteNumber,
    string Reason,
    string RequestKey,
    IReadOnlyList<PostSalesCreditNoteLine> Lines,
    DateOnly? PostingDate = null,
    string? Notes = null);
