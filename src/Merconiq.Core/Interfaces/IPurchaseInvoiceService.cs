using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Posts supplier invoices matched to posted goods receipts: clears GRNI, recognizes the payable
/// liability exactly once, and derives open-payable balances from posted evidence.
/// </summary>
public interface IPurchaseInvoiceService
{
    Task<PurchaseInvoiceResult> PostPurchaseInvoiceAsync(
        PostPurchaseInvoiceRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PurchaseInvoice?> GetPurchaseInvoiceAsync(
        int companyId,
        int purchaseInvoiceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseInvoice>> ListPurchaseInvoicesAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Open payable items derived from posted invoices minus posted allocations.</summary>
    Task<PayablesOpenSummary> GetPayablesOpenAsync(
        int companyId,
        int? supplierId = null,
        CancellationToken cancellationToken = default);
}
