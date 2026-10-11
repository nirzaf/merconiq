using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

public sealed class IntercompanyService(
    InventoryDbContext db,
    ITenantContext tenantContext) : IIntercompanyService
{
    public async Task<IntercompanyRelationshipView> ConfigureRelationshipAsync(
        ConfigureIntercompanyRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ParentCompanyId == request.SubsidiaryCompanyId)
            throw new ArgumentException("Parent and subsidiary companies must be distinct.");

        var parent = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == request.ParentCompanyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Parent company not found in this tenant.");

        var subsidiary = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == request.SubsidiaryCompanyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Subsidiary company not found in this tenant.");

        var existing = await db.IntercompanyRelationships.FirstOrDefaultAsync(
            r => r.TenantId == tenantContext.TenantId &&
                 r.ParentCompanyId == request.ParentCompanyId &&
                 r.SubsidiaryCompanyId == request.SubsidiaryCompanyId,
            cancellationToken);

        if (existing is null)
        {
            existing = new IntercompanyRelationship
            {
                TenantId = tenantContext.TenantId,
                ParentCompanyId = request.ParentCompanyId,
                SubsidiaryCompanyId = request.SubsidiaryCompanyId,
                ParentReceivableAccountId = request.ParentReceivableAccountId,
                ParentRevenueAccountId = request.ParentRevenueAccountId,
                SubsidiaryPayableAccountId = request.SubsidiaryPayableAccountId,
                SubsidiaryExpenseAccountId = request.SubsidiaryExpenseAccountId,
                IsActive = true
            };
            db.IntercompanyRelationships.Add(existing);
        }
        else
        {
            existing.ParentReceivableAccountId = request.ParentReceivableAccountId;
            existing.ParentRevenueAccountId = request.ParentRevenueAccountId;
            existing.SubsidiaryPayableAccountId = request.SubsidiaryPayableAccountId;
            existing.SubsidiaryExpenseAccountId = request.SubsidiaryExpenseAccountId;
            existing.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new IntercompanyRelationshipView(
            existing.Id,
            existing.ParentCompanyId,
            existing.SubsidiaryCompanyId,
            existing.ParentReceivableAccountId,
            existing.ParentRevenueAccountId,
            existing.SubsidiaryPayableAccountId,
            existing.SubsidiaryExpenseAccountId,
            existing.IsActive);
    }

    public async Task<IntercompanyTransactionView> PostTransactionAsync(
        PostIntercompanyTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be positive.");

        var relationship = await db.IntercompanyRelationships.FirstOrDefaultAsync(
            r => r.Id == request.RelationshipId && r.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Intercompany relationship not found.");

        if (!relationship.IsActive)
            throw new InvalidOperationException("Intercompany relationship is inactive.");

        var existingTx = await db.IntercompanyTransactions.FirstOrDefaultAsync(
            t => t.TenantId == tenantContext.TenantId && t.ReferenceKey == request.ReferenceKey, cancellationToken);

        if (existingTx is not null)
            throw new InvalidOperationException($"Intercompany transaction with reference {request.ReferenceKey} already exists.");

        var tx = new IntercompanyTransaction
        {
            TenantId = tenantContext.TenantId,
            RelationshipId = request.RelationshipId,
            SourceCompanyId = request.SourceCompanyId,
            TargetCompanyId = request.TargetCompanyId,
            SourceDocumentType = request.SourceDocumentType,
            SourceDocumentId = request.SourceDocumentId,
            TargetDocumentType = "PurchaseInvoice",
            TargetDocumentId = null,
            ReferenceKey = request.ReferenceKey,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Status = IntercompanyTransactionStatus.Reconciled,
            Notes = request.Notes
        };

        db.IntercompanyTransactions.Add(tx);
        await db.SaveChangesAsync(cancellationToken);

        return new IntercompanyTransactionView(
            tx.Id,
            tx.RelationshipId,
            tx.SourceCompanyId,
            tx.TargetCompanyId,
            tx.SourceDocumentType,
            tx.SourceDocumentId,
            tx.TargetDocumentType,
            tx.TargetDocumentId,
            tx.ReferenceKey,
            tx.Amount,
            tx.CurrencyCode,
            tx.Status.ToString(),
            tx.Notes);
    }

    public async Task<IntercompanyEliminationReport> GetEliminationReportAsync(
        int parentCompanyId,
        int subsidiaryCompanyId,
        CancellationToken cancellationToken = default)
    {
        var transactions = await db.IntercompanyTransactions
            .Where(t => t.TenantId == tenantContext.TenantId &&
                        ((t.SourceCompanyId == parentCompanyId && t.TargetCompanyId == subsidiaryCompanyId) ||
                         (t.SourceCompanyId == subsidiaryCompanyId && t.TargetCompanyId == parentCompanyId)))
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var parentReceivableTotal = transactions
            .Where(t => t.SourceCompanyId == parentCompanyId)
            .Sum(t => t.Amount);

        var subsidiaryPayableTotal = transactions
            .Where(t => t.TargetCompanyId == subsidiaryCompanyId)
            .Sum(t => t.Amount);

        var variance = Math.Abs(parentReceivableTotal - subsidiaryPayableTotal);
        var isBalanced = variance == 0;

        var views = transactions.Select(tx => new IntercompanyTransactionView(
            tx.Id,
            tx.RelationshipId,
            tx.SourceCompanyId,
            tx.TargetCompanyId,
            tx.SourceDocumentType,
            tx.SourceDocumentId,
            tx.TargetDocumentType,
            tx.TargetDocumentId,
            tx.ReferenceKey,
            tx.Amount,
            tx.CurrencyCode,
            tx.Status.ToString(),
            tx.Notes)).ToList();

        return new IntercompanyEliminationReport(
            parentCompanyId,
            subsidiaryCompanyId,
            parentReceivableTotal,
            subsidiaryPayableTotal,
            variance,
            isBalanced,
            views);
    }
}
