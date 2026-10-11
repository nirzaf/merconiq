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
/// Reads and changes customer credit limits. Changes run under the company's credit advisory lock, the same
/// lock invoice posting takes, so a limit change and an invoice check cannot interleave.
/// </summary>
public sealed class CustomerCreditService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<CustomerCreditService> logger) : ICustomerCreditService
{
    /// <inheritdoc />
    public async Task<CustomerCreditExposure> GetExposureAsync(
        int companyId,
        int customerId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var customer = await FindCustomerAsync(companyId, customerId, cancellationToken);
        return await ExposureAsync(customer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CustomerCreditExposure> SetCreditLimitAsync(
        int companyId,
        int customerId,
        decimal? creditLimit,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        Customer updated = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"customer-credit:{companyId}", cancellationToken);

            var company = await context.Companies.SingleOrDefaultAsync(
                    candidate => candidate.Id == companyId && candidate.IsActive,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Active company not found.");
            CustomerCreditRules.ValidateCreditLimit(creditLimit, company.CurrencyScale ?? 2);

            var customer = await FindCustomerAsync(companyId, customerId, cancellationToken);
            customer.CreditLimit = creditLimit;
            customer.CreditLimitChangeReason = normalizedReason;
            customer.CreditLimitChangedBy = actor.Trim();
            customer.CreditLimitChangedAtUtc = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            updated = customer;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Credit limit for customer {CustomerId} of company {CompanyId} set to {CreditLimit}; reason recorded",
            customerId, companyId, creditLimit?.ToString() ?? "none");
        return await ExposureAsync(updated, cancellationToken);
    }

    private async Task<CustomerCreditExposure> ExposureAsync(Customer customer, CancellationToken cancellationToken)
    {
        var open = await CustomerReceivableExposure.OpenReceivableAsync(
            context, customer.CompanyId, customer.Id, cancellationToken);
        return new CustomerCreditExposure(
            customer.Id,
            customer.CustomerCode,
            customer.Name,
            customer.CreditLimit,
            open,
            CustomerCreditRules.Available(customer.CreditLimit, open));
    }

    private async Task<Customer> FindCustomerAsync(
        int companyId,
        int customerId,
        CancellationToken cancellationToken) =>
        await context.Customers.SingleOrDefaultAsync(
            candidate => candidate.Id == customerId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Customer not found in this company.");

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
