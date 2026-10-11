using Merconiq.Core.Exceptions;

namespace Merconiq.Core.Services;

/// <summary>
/// Pure settlement arithmetic: a batch's net is its gross less the processor fee, and a batch can never pay
/// out more tender value than its gross.
/// </summary>
public static class CardSettlementRules
{
    /// <summary>Validates gross and fee at the company's scale and returns the net amount paid out.</summary>
    public static decimal ValidateBatch(decimal grossAmount, decimal feeAmount, int currencyScale)
    {
        if (grossAmount <= 0m)
            throw new FinancePostingValidationException("A settlement batch must have a positive gross amount.");
        if (feeAmount < 0m || feeAmount > grossAmount)
            throw new FinancePostingValidationException("The processor fee must be zero or more and no more than the gross.");
        if (grossAmount != Math.Round(grossAmount, currencyScale, MidpointRounding.ToEven) ||
            feeAmount != Math.Round(feeAmount, currencyScale, MidpointRounding.ToEven))
            throw new FinancePostingValidationException(
                $"Settlement amounts must be expressed at {currencyScale} decimal places.");
        return grossAmount - feeAmount;
    }

    /// <summary>Gross value still available for matching tenders to this batch.</summary>
    public static decimal RemainingGross(decimal grossAmount, decimal matchedAmount) =>
        grossAmount - matchedAmount;
}
