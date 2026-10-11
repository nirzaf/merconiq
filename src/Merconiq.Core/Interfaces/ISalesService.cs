using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Order-to-cash posting: approved sales orders (commitments only), stock-issuing deliveries
/// with exact moving-average COGS journals, and sales invoices that recognize revenue and AR
/// exactly once. Each operation is idempotent per request key.
/// </summary>
public interface ISalesService
{
    Task<SalesOrderResult> PostSalesOrderAsync(
        PostSalesOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> PostDeliveryAsync(
        PostDeliveryRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceResult> PostSalesInvoiceAsync(
        PostSalesInvoiceRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<SalesOrder?> GetSalesOrderAsync(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Delivery progress for one order, derived from posted delivery lines.</summary>
    Task<SalesOrderProgress?> GetSalesOrderProgressAsync(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Customer aging as of a date: open receivable balances bucketed by days past due.</summary>
    Task<AgingReport> GetReceivablesAgingAsync(
        int companyId,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates one open quotation; prices are snapshotted and never post.</summary>
    Task<QuotationResult> PostQuotationAsync(
        PostQuotationRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts one open, unexpired quotation into a sales order bounded by the quoted lines.
    /// Conversion is idempotent: repeated calls return the original order.
    /// </summary>
    Task<QuotationConversionResult> ConvertQuotationAsync(
        ConvertQuotationRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Quotation?> GetQuotationAsync(
        int companyId,
        int quotationId,
        CancellationToken cancellationToken = default);
}
