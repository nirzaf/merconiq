using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Point-of-sale operation: register-bound shifts, idempotent checkouts that issue valued stock
/// and split tenders into cash/bank journals with exact COGS, refunds bounded by the
/// sold-minus-refunded balance, and shift close with cash reconciliation and over/short posting.
/// </summary>
public interface IPosService
{
    Task<PosRegister> CreateRegisterAsync(
        int companyId, int locationId, string name, string actor, CancellationToken cancellationToken = default);

    Task<PosShift> OpenShiftAsync(
        OpenPosShiftRequest request, string actor, CancellationToken cancellationToken = default);

    Task<PosSaleResult> PostSaleAsync(
        PostPosSaleRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PosRefundResult> PostRefundAsync(
        PostPosRefundRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PosShiftCloseResult> CloseShiftAsync(
        ClosePosShiftRequest request, string actor, CancellationToken cancellationToken = default);
}
