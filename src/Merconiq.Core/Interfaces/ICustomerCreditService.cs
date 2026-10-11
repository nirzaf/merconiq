using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Customer credit limits and open exposure. The exposure is the same open receivable that sales invoicing
/// checks: posted invoices less payment allocations and posted sales credit notes for that customer.
/// </summary>
public interface ICustomerCreditService
{
    /// <summary>Reads one customer's limit, open receivable and remaining headroom.</summary>
    Task<CustomerCreditExposure> GetExposureAsync(
        int companyId,
        int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or clears a customer's credit limit with a mandatory reason; the change is audited.</summary>
    Task<CustomerCreditExposure> SetCreditLimitAsync(
        int companyId,
        int customerId,
        decimal? creditLimit,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);
}
