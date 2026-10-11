using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Posts supplier payments against payables: allocates money to open invoices and commits the
/// balanced AP/bank journal exactly once per request key. Advances and internal transfers are
/// future increments; customer-receipt payments arrive with the sales/AR slice.
/// </summary>
public interface IPaymentService
{
    Task<PaymentResult> PostPaymentAsync(
        PostPaymentRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PaymentEntry?> GetPaymentAsync(
        int companyId,
        int paymentEntryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentEntry>> ListPaymentsAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
