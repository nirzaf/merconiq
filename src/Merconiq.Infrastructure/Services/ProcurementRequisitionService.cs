using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class ProcurementRequisitionService(
    InventoryDbContext context,
    ILogger<ProcurementRequisitionService> logger) : IProcurementRequisitionService
{
    public async Task<MaterialRequestResult> CreateMaterialRequestAsync(
        CreateMaterialRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (request.Lines.Count == 0)
            throw new FinancePostingValidationException("At least one line item is required.");

        var requestNumber = $"MR-{request.CompanyId}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

        var mr = new MaterialRequest
        {
            CompanyId = request.CompanyId,
            RequestNumber = requestNumber,
            RequestedDate = request.RequestedDate,
            RequiredDate = request.RequiredDate,
            Purpose = request.Purpose,
            Status = MaterialRequestStatus.Draft,
            RequestedBy = request.RequestedBy,
            Notes = request.Notes
        };

        foreach (var line in request.Lines)
        {
            mr.Lines.Add(new MaterialRequestLine
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                OrderedQuantity = 0
            });
        }

        context.MaterialRequests.Add(mr);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Created MaterialRequest {RequestNumber}", mr.RequestNumber);

        return new MaterialRequestResult(mr.Id, mr.RequestNumber, mr.Status, mr.Lines.Count);
    }

    public async Task<MaterialRequest> ApproveMaterialRequestAsync(
        int companyId,
        int materialRequestId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var mr = await context.MaterialRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == materialRequestId && r.CompanyId == companyId, cancellationToken)
            ?? throw new FinancePostingValidationException("MaterialRequest not found.");

        if (mr.Status != MaterialRequestStatus.Draft)
            throw new FinancePostingValidationException($"Cannot approve MaterialRequest in {mr.Status} status.");

        mr.Status = MaterialRequestStatus.Approved;
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Approved MaterialRequest {RequestNumber}", mr.RequestNumber);
        return mr;
    }

    public async Task<SupplierQuotation> CreateSupplierQuotationAsync(
        CreateSupplierQuotationRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (request.SupplierId <= 0)
            throw new FinancePostingValidationException("A valid SupplierId is required.");
        if (request.Lines.Count == 0)
            throw new FinancePostingValidationException("At least one quotation line is required.");

        var quoteNumber = $"SQ-{request.CompanyId}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        var totalAmount = request.Lines.Sum(l => l.Quantity * l.Rate);

        var quote = new SupplierQuotation
        {
            CompanyId = request.CompanyId,
            QuotationNumber = quoteNumber,
            SupplierId = request.SupplierId,
            QuotationDate = request.QuotationDate,
            ValidUntil = request.ValidUntil,
            Status = SupplierQuotationStatus.Submitted,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
            TotalAmount = totalAmount,
            Notes = request.Notes
        };

        foreach (var line in request.Lines)
        {
            quote.Lines.Add(new SupplierQuotationLine
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity,
                Rate = line.Rate,
                TotalAmount = line.Quantity * line.Rate
            });
        }

        context.SupplierQuotations.Add(quote);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Created SupplierQuotation {QuoteNumber} for Supplier {SupplierId}", quote.QuotationNumber, quote.SupplierId);
        return quote;
    }

    public async Task<SupplierQuotationComparisonReport> CompareSupplierQuotationsAsync(
        int companyId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0 || itemId <= 0)
            throw new FinancePostingValidationException("Valid CompanyId and ItemId are required.");

        var item = await context.Items.FindAsync([itemId], cancellationToken)
            ?? throw new FinancePostingValidationException($"Item {itemId} not found.");

        var quoteLines = await context.SupplierQuotationLines
            .Include(sql => sql.SupplierQuotation)
            .ThenInclude(sq => sq.Supplier)
            .Where(sql => sql.ItemId == itemId &&
                         sql.SupplierQuotation.CompanyId == companyId &&
                         sql.SupplierQuotation.Status != SupplierQuotationStatus.Rejected)
            .OrderBy(sql => sql.Rate)
            .ToListAsync(cancellationToken);

        var items = quoteLines.Select(ql => new SupplierQuotationComparisonItem(
            ql.SupplierQuotation.SupplierId,
            ql.SupplierQuotation.Supplier.Name,
            ql.SupplierQuotation.Id,
            ql.SupplierQuotation.QuotationNumber,
            ql.SupplierQuotation.QuotationDate,
            ql.Rate,
            ql.TotalAmount)).ToList();

        return new SupplierQuotationComparisonReport(item.Id, item.ItemCode, items);
    }
}
