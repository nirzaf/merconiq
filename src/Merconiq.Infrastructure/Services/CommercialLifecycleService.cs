using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class CommercialLifecycleService(
    InventoryDbContext context,
    ILogger<CommercialLifecycleService> logger) : ICommercialLifecycleService
{
    public async Task<SubcontractingOrder> CreateSubcontractingOrderAsync(
        CreateSubcontractingOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.SupplierId <= 0 || request.FinishedItemId <= 0)
            throw new FinancePostingValidationException("CompanyId, SupplierId, and FinishedItemId are required.");

        var totalService = request.Quantity * request.ServiceRate;
        var order = new SubcontractingOrder
        {
            CompanyId = request.CompanyId,
            SupplierId = request.SupplierId,
            FinishedItemId = request.FinishedItemId,
            QuantityToProduce = request.Quantity,
            ServiceRate = request.ServiceRate,
            TotalServiceCost = totalService,
            Status = SubcontractingStatus.Draft
        };

        foreach (var comp in request.Components)
        {
            order.SuppliedItems.Add(new SubcontractingSuppliedItem
            {
                ComponentItemId = comp.ComponentItemId,
                RequiredQuantity = comp.RequiredQuantity,
                TransferredQuantity = 0,
                ConsumedQuantity = 0
            });
        }

        context.SubcontractingOrders.Add(order);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Created SubcontractingOrder {Id} for Item {ItemId}", order.Id, request.FinishedItemId);
        return order;
    }

    public async Task<SalesPartnerCommission> AccrueCommissionAsync(
        CalculateCommissionRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.SalesInvoiceId <= 0)
            throw new FinancePostingValidationException("CompanyId and SalesInvoiceId are required.");
        if (string.IsNullOrWhiteSpace(request.PartnerName))
            throw new FinancePostingValidationException("PartnerName is required.");

        var invoice = await context.SalesInvoices.FindAsync([request.SalesInvoiceId], cancellationToken)
            ?? throw new FinancePostingValidationException("SalesInvoice not found.");

        var commissionAmount = Math.Round(invoice.TotalValue * (request.CommissionPercentage / 100m), 4);

        var commission = new SalesPartnerCommission
        {
            CompanyId = request.CompanyId,
            PartnerName = request.PartnerName.Trim(),
            CommissionPercentage = request.CommissionPercentage,
            SalesInvoiceId = request.SalesInvoiceId,
            InvoicedAmount = invoice.TotalValue,
            CommissionAmount = commissionAmount,
            IsPaid = false
        };

        context.SalesPartnerCommissions.Add(commission);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Accrued commission of {Amt} for partner {Partner}", commissionAmount, request.PartnerName);
        return commission;
    }

    public async Task<DunningNotice> IssueDunningNoticeAsync(
        IssueDunningNoticeRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.SalesInvoiceId <= 0)
            throw new FinancePostingValidationException("CompanyId and SalesInvoiceId are required.");

        var invoice = await context.SalesInvoices.FindAsync([request.SalesInvoiceId], cancellationToken)
            ?? throw new FinancePostingValidationException("SalesInvoice not found.");

        var paid = await context.PaymentAllocations
            .Where(a => a.SalesInvoiceId == request.SalesInvoiceId)
            .SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0m;

        var openAmount = invoice.TotalValue - paid;
        if (openAmount <= 0)
            throw new FinancePostingValidationException("Invoice has no outstanding balance.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var notice = new DunningNotice
        {
            CompanyId = request.CompanyId,
            CustomerId = invoice.CustomerId,
            SalesInvoiceId = invoice.Id,
            Level = request.Level,
            OutstandingAmount = openAmount,
            DunningFee = request.LateFee,
            InterestAmount = request.Interest,
            NoticeDate = today,
            DueDate = today.AddDays(7)
        };

        context.DunningNotices.Add(notice);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Issued DunningNotice level {Level} for Invoice {InvoiceId}", request.Level, invoice.Id);
        return notice;
    }
}
