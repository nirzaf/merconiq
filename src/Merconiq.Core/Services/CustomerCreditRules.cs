using Merconiq.Core.Exceptions;

namespace Merconiq.Core.Services;

/// <summary>
/// Pure credit-limit arithmetic. A limit caps the customer's open receivable in base currency; an invoice
/// that would take the open amount above the cap needs an explicit reviewer override with a reason.
/// </summary>
public static class CustomerCreditRules
{
    /// <summary>Rejects negative or off-scale limits; null means the customer has no limit.</summary>
    public static void ValidateCreditLimit(decimal? creditLimit, int currencyScale)
    {
        if (creditLimit is null)
            return;
        if (creditLimit < 0m)
            throw new FinancePostingValidationException("A credit limit cannot be negative.");
        if (creditLimit.Value != Math.Round(creditLimit.Value, currencyScale, MidpointRounding.ToEven))
            throw new FinancePostingValidationException(
                $"The credit limit must be expressed at {currencyScale} decimal places.");
    }

    /// <summary>True when a limit exists and the projected open receivable is above it.</summary>
    public static bool WouldExceed(decimal? creditLimit, decimal projectedOpenReceivable) =>
        creditLimit is { } limit && projectedOpenReceivable > limit;

    /// <summary>Headroom left under the limit, or null when the customer has no limit.</summary>
    public static decimal? Available(decimal? creditLimit, decimal openReceivable) =>
        creditLimit is { } limit ? limit - openReceivable : null;
}
