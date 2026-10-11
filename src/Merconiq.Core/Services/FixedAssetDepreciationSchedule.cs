using Merconiq.Core.Exceptions;

namespace Merconiq.Core.Services;

/// <summary>One month of a straight-line depreciation schedule.</summary>
public sealed record FixedAssetDepreciationPeriod(
    int Index,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Amount);

/// <summary>
/// Straight-line schedule over whole months with a full-month convention. The monthly charge is rounded
/// to the company's currency scale and the final month absorbs the rounding remainder, so the schedule
/// always sums to exactly cost minus salvage value.
/// </summary>
public static class FixedAssetDepreciationSchedule
{
    /// <summary>Upper bound on useful life, which keeps one schedule a bounded list.</summary>
    public const int MaximumUsefulLifeMonths = 600;

    /// <summary>Builds the monthly periods; throws when the parameters cannot produce a positive schedule.</summary>
    public static IReadOnlyList<FixedAssetDepreciationPeriod> Build(
        decimal costAmount,
        decimal salvageValue,
        int usefulLifeMonths,
        DateOnly depreciationStartDate,
        int currencyScale)
    {
        if (costAmount <= 0m)
            throw new FinancePostingValidationException("The asset cost must be positive.");
        AssertAtScale(costAmount, currencyScale, "cost");
        AssertAtScale(salvageValue, currencyScale, "salvage value");
        if (salvageValue < 0m || salvageValue >= costAmount)
            throw new FinancePostingValidationException("The salvage value must be zero or more and less than the cost.");
        if (usefulLifeMonths is < 1 or > MaximumUsefulLifeMonths)
            throw new FinancePostingValidationException(
                $"The useful life must be between 1 and {MaximumUsefulLifeMonths} months.");

        var depreciable = costAmount - salvageValue;
        var monthly = Math.Round(depreciable / usefulLifeMonths, currencyScale, MidpointRounding.ToEven);
        if (monthly <= 0m)
            throw new FinancePostingValidationException(
                "The depreciable amount is too small to spread over the useful life at this currency scale.");

        var firstMonth = new DateOnly(depreciationStartDate.Year, depreciationStartDate.Month, 1);
        var periods = new List<FixedAssetDepreciationPeriod>(usefulLifeMonths);
        var allocated = 0m;
        for (var index = 1; index <= usefulLifeMonths; index++)
        {
            var periodStart = firstMonth.AddMonths(index - 1);
            var periodEnd = new DateOnly(
                periodStart.Year, periodStart.Month, DateTime.DaysInMonth(periodStart.Year, periodStart.Month));
            var amount = index == usefulLifeMonths ? depreciable - allocated : monthly;
            if (amount <= 0m)
                throw new FinancePostingValidationException(
                    "The depreciable amount cannot be spread evenly over the useful life at this currency scale.");

            allocated += amount;
            periods.Add(new FixedAssetDepreciationPeriod(index, periodStart, periodEnd, amount));
        }

        return periods;
    }

    private static void AssertAtScale(decimal amount, int currencyScale, string label)
    {
        if (amount != Math.Round(amount, currencyScale, MidpointRounding.ToEven))
            throw new FinancePostingValidationException(
                $"The {label} must be expressed at {currencyScale} decimal places.");
    }
}
