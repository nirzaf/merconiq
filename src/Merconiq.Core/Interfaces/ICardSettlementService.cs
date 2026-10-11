using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Matches card tenders to acquirer settlement batches. Reconciliation reads and records matches only; it
/// never posts a journal, because the card tender already posted to the bank account at checkout.
/// </summary>
public interface ICardSettlementService
{
    /// <summary>Imports one settlement batch; the processor reference must be unique within the company.</summary>
    Task<CardSettlementBatch> ImportBatchAsync(
        CardSettlementBatchImportRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the company's settlement batches, newest settlement date first.</summary>
    Task<IReadOnlyList<CardSettlementBatch>> ListBatchesAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    /// <summary>Attributes one card tender to a batch; the tender must not be paid by another active batch.</summary>
    Task<CardSettlementMatch> MatchTenderAsync(
        int companyId,
        int batchId,
        int posSaleTenderId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws a tender's match with a mandatory reason; the withdrawn match stays on record.</summary>
    Task<CardSettlementMatch> UnmatchTenderAsync(
        int companyId,
        int batchId,
        int posSaleTenderId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles one batch: what it has paid, what remains, and which card tenders still await it.</summary>
    Task<CardSettlementReconciliation?> GetReconciliationAsync(
        int companyId,
        int batchId,
        CancellationToken cancellationToken = default);
}
