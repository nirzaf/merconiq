using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// The open receivable one customer owes the company: posted sales invoices, less payment allocations,
/// less posted sales credit notes. Credit checks and credit-exposure reads share this one definition so
/// they cannot disagree. Unapplied advances are not netted, which keeps the figure conservative.
/// </summary>
public static class CustomerReceivableExposure
{
    public static async Task<decimal> OpenReceivableAsync(
        InventoryDbContext context,
        int companyId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var invoiced = await context.SalesInvoices
            .Where(invoice => invoice.CompanyId == companyId && invoice.CustomerId == customerId)
            .SumAsync(invoice => (decimal?)invoice.TotalValue, cancellationToken) ?? 0m;
        var allocated = await context.PaymentAllocations
            .Where(allocation => allocation.CompanyId == companyId &&
                                 allocation.SalesInvoiceId != null &&
                                 allocation.SalesInvoice!.CustomerId == customerId)
            .SumAsync(allocation => (decimal?)allocation.Amount, cancellationToken) ?? 0m;
        var creditNoted = await context.SalesCreditNotes
            .Where(note => note.CompanyId == companyId && note.CustomerId == customerId)
            .SumAsync(note => (decimal?)note.TotalValue, cancellationToken) ?? 0m;
        return invoiced - allocated - creditNoted;
    }
}
