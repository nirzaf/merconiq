using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Posts goods receipts against approved purchase orders: valued stock intake plus the balanced
/// Inventory/GRNI journal, committed atomically once per request key.
/// </summary>
public interface IGoodsReceiptService
{
    /// <summary>
    /// Validates and posts one receipt. Accepted quantities enter the moving-average stock
    /// ledger at the line unit cost and one journal (Dr Inventory / Cr GRNI) commits through the
    /// finance kernel. A committed request key replays the stored receipt; a conflicting payload
    /// is rejected.
    /// </summary>
    Task<GoodsReceiptResult> PostGoodsReceiptAsync(
        PostGoodsReceiptRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one company-owned posted receipt with its lines, or <see langword="null"/>.</summary>
    Task<GoodsReceipt?> GetGoodsReceiptAsync(
        int companyId,
        int goodsReceiptId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists posted receipts for one company, newest first, bounded by limit.</summary>
    Task<IReadOnlyList<GoodsReceipt>> ListGoodsReceiptsAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
