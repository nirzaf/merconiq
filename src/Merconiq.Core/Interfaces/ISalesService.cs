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
}
