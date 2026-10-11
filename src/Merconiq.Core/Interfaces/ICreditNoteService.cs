using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Posts bounded returns and credit notes against posted receipt/delivery evidence: supplier
/// credit notes (Dr AP / Cr Inventory) and sales credit notes (Dr Sales / Cr AR plus inventory
/// restock). Both are idempotent per request key and immutable.
/// </summary>
public interface ICreditNoteService
{
    Task<PurchaseCreditNote> PostPurchaseCreditNoteAsync(
        PostPurchaseCreditNoteRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);

    Task<SalesCreditNote> PostSalesCreditNoteAsync(
        PostSalesCreditNoteRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);
}
