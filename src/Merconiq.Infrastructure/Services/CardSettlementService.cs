using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Card settlement reconciliation. Every write runs under one company-scoped advisory lock and the database
/// keeps one active settlement per card tender, so concurrent reviewers cannot pay a tender twice.
/// </summary>
public sealed class CardSettlementService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<CardSettlementService> logger) : ICardSettlementService
{
    private const int MaximumListedAwaitingTenders = 200;

    /// <inheritdoc />
    public async Task<CardSettlementBatch> ImportBatchAsync(
        CardSettlementBatchImportRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(request.CompanyId);
        var reference = request.Reference?.Trim() ?? string.Empty;
        if (reference.Length is 0 or > 100)
            throw new FinancePostingValidationException("A processor reference of 1 to 100 characters is required.");

        CardSettlementBatch created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"card-settlement:{request.CompanyId}", cancellationToken);

            var company = await context.Companies.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.CompanyId && candidate.IsActive,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Active company not found.");
            var net = CardSettlementRules.ValidateBatch(request.GrossAmount, request.FeeAmount, company.CurrencyScale ?? 2);

            var duplicate = await context.CardSettlementBatches.AnyAsync(
                batch => batch.CompanyId == request.CompanyId && batch.Reference == reference,
                cancellationToken);
            if (duplicate)
                throw new FinancePostingConflictException($"A settlement batch '{reference}' is already imported.");

            var batch = new CardSettlementBatch
            {
                CompanyId = request.CompanyId,
                Reference = reference,
                SettlementDate = request.SettlementDate,
                GrossAmount = request.GrossAmount,
                FeeAmount = request.FeeAmount,
                NetAmount = net,
                ImportedBy = actor.Trim(),
                ImportedAtUtc = DateTime.UtcNow
            };
            context.CardSettlementBatches.Add(batch);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = batch;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Imported card settlement batch {Reference} for company {CompanyId}: gross {Gross}, fee {Fee}",
            created.Reference, request.CompanyId, created.GrossAmount, created.FeeAmount);
        return created;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CardSettlementBatch>> ListBatchesAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.CardSettlementBatches
            .Where(batch => batch.CompanyId == companyId)
            .OrderByDescending(batch => batch.SettlementDate)
            .ThenByDescending(batch => batch.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CardSettlementMatch> MatchTenderAsync(
        int companyId,
        int batchId,
        int posSaleTenderId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        CardSettlementMatch created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"card-settlement:{companyId}", cancellationToken);

            var batch = await FindBatchAsync(companyId, batchId, cancellationToken);
            var tender = await context.PosSaleTenders
                .Include(candidate => candidate.PosSale)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == posSaleTenderId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Tender not found in this company.");
            if (tender.Method != PaymentMethod.Card)
                throw new FinancePostingValidationException(
                    "Only card tenders are settled by processor batches.");
            if (tender.PosSale.PostingDate > batch.SettlementDate)
                throw new FinancePostingValidationException(
                    $"The card tender was taken on {tender.PosSale.PostingDate:yyyy-MM-dd}, after the batch settlement date {batch.SettlementDate:yyyy-MM-dd}.");

            var alreadySettled = await context.CardSettlementMatches.AnyAsync(
                match => match.CompanyId == companyId && match.IsActive && match.PosSaleTenderId == tender.Id,
                cancellationToken);
            if (alreadySettled)
                throw new FinancePostingConflictException(
                    "This card tender is already settled by a batch; withdraw that match before settling it again.");

            var matched = await MatchedAmountAsync(companyId, batch.Id, cancellationToken);
            var remaining = CardSettlementRules.RemainingGross(batch.GrossAmount, matched);
            if (tender.Amount > remaining)
                throw new FinancePostingValidationException(
                    $"Settling {tender.Amount} would exceed the batch's remaining gross {remaining}.");

            var match = new CardSettlementMatch
            {
                CompanyId = companyId,
                CardSettlementBatchId = batch.Id,
                PosSaleTenderId = tender.Id,
                Amount = tender.Amount,
                IsActive = true,
                MatchedBy = actor.Trim(),
                MatchedAtUtc = DateTime.UtcNow
            };
            context.CardSettlementMatches.Add(match);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = match;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Settled card tender {PosSaleTenderId} with batch {BatchId} for company {CompanyId}",
            posSaleTenderId, batchId, companyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<CardSettlementMatch> UnmatchTenderAsync(
        int companyId,
        int batchId,
        int posSaleTenderId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        CardSettlementMatch withdrawn = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"card-settlement:{companyId}", cancellationToken);

            await FindBatchAsync(companyId, batchId, cancellationToken);
            var match = await context.CardSettlementMatches.SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId &&
                             candidate.IsActive &&
                             candidate.CardSettlementBatchId == batchId &&
                             candidate.PosSaleTenderId == posSaleTenderId,
                cancellationToken)
                ?? throw new FinancePostingConflictException("This card tender is not settled by this batch.");

            match.IsActive = false;
            match.UnmatchedBy = actor.Trim();
            match.UnmatchedAtUtc = DateTime.UtcNow;
            match.UnmatchReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            withdrawn = match;
        }, cancellationToken: cancellationToken);

        logger.LogWarning(
            "Withdrew settlement of card tender {PosSaleTenderId} from batch {BatchId} for company {CompanyId}; reason recorded",
            posSaleTenderId, batchId, companyId);
        return withdrawn;
    }

    /// <inheritdoc />
    public async Task<CardSettlementReconciliation?> GetReconciliationAsync(
        int companyId,
        int batchId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var batch = await context.CardSettlementBatches.SingleOrDefaultAsync(
            candidate => candidate.Id == batchId && candidate.CompanyId == companyId,
            cancellationToken);
        if (batch is null)
            return null;

        var matched = await MatchedAmountAsync(companyId, batch.Id, cancellationToken);
        var settledTenderIds = context.CardSettlementMatches
            .Where(match => match.CompanyId == companyId && match.IsActive)
            .Select(match => match.PosSaleTenderId);
        var awaiting = context.PosSaleTenders
            .Where(tender => tender.CompanyId == companyId &&
                             tender.Method == PaymentMethod.Card &&
                             tender.PosSale.PostingDate <= batch.SettlementDate &&
                             !settledTenderIds.Contains(tender.Id));
        var awaitingCount = await awaiting.CountAsync(cancellationToken);
        var awaitingAmount = await awaiting.SumAsync(tender => (decimal?)tender.Amount, cancellationToken) ?? 0m;
        var awaitingTenders = await awaiting
            .OrderBy(tender => tender.PosSale.PostingDate)
            .ThenBy(tender => tender.Id)
            .Take(MaximumListedAwaitingTenders)
            .Select(tender => new AwaitingCardTender(
                tender.Id, tender.PosSaleId, tender.PosSale.PostingDate, tender.Amount))
            .ToListAsync(cancellationToken);

        return new CardSettlementReconciliation(
            batch,
            matched,
            CardSettlementRules.RemainingGross(batch.GrossAmount, matched),
            matched == batch.GrossAmount,
            awaitingCount,
            awaitingAmount,
            awaitingTenders);
    }

    private async Task<decimal> MatchedAmountAsync(int companyId, int batchId, CancellationToken cancellationToken) =>
        await context.CardSettlementMatches
            .Where(match => match.CompanyId == companyId &&
                            match.IsActive &&
                            match.CardSettlementBatchId == batchId)
            .SumAsync(match => (decimal?)match.Amount, cancellationToken) ?? 0m;

    private async Task<CardSettlementBatch> FindBatchAsync(
        int companyId,
        int batchId,
        CancellationToken cancellationToken) =>
        await context.CardSettlementBatches.SingleOrDefaultAsync(
            candidate => candidate.Id == batchId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Settlement batch not found in this company.");

    private static void RequirePositiveCompany(int companyId)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reason of 1 to 500 characters is required.");
        return trimmed;
    }
}
