using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Straight-line schedule arithmetic: monthly periods end on real month ends, the schedule always sums to
/// cost minus salvage, rounding remainders land in the final month, and impossible parameters are refused.
/// </summary>
public class FixedAssetDepreciationScheduleTests
{
    [Fact]
    public void Build_SpreadsEvenlyOverWholeMonthsEndingOnMonthEnds()
    {
        var periods = FixedAssetDepreciationSchedule.Build(
            costAmount: 1200m, salvageValue: 0m, usefulLifeMonths: 12,
            depreciationStartDate: new DateOnly(2026, 1, 1), currencyScale: 2);

        periods.Should().HaveCount(12);
        periods.Should().OnlyContain(period => period.Amount == 100m);
        periods[0].Should().Be(new FixedAssetDepreciationPeriod(1, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 100m));
        periods[1].PeriodEnd.Should().Be(new DateOnly(2026, 2, 28), "2026 is not a leap year");
        periods[11].PeriodEnd.Should().Be(new DateOnly(2026, 12, 31));
        periods.Sum(period => period.Amount).Should().Be(1200m);
    }

    [Fact]
    public void Build_PutTheRoundingRemainderInTheFinalMonth()
    {
        var periods = FixedAssetDepreciationSchedule.Build(
            costAmount: 100m, salvageValue: 0m, usefulLifeMonths: 3,
            depreciationStartDate: new DateOnly(2026, 1, 1), currencyScale: 2);

        periods.Select(period => period.Amount).Should().Equal(33.33m, 33.33m, 33.34m);
        periods.Sum(period => period.Amount).Should().Be(100m);
    }

    [Fact]
    public void Build_NeverDepreciatesBelowSalvageValue()
    {
        var periods = FixedAssetDepreciationSchedule.Build(
            costAmount: 1000m, salvageValue: 100m, usefulLifeMonths: 9,
            depreciationStartDate: new DateOnly(2026, 1, 1), currencyScale: 2);

        periods.Should().OnlyContain(period => period.Amount == 100m);
        periods.Sum(period => period.Amount).Should().Be(900m);
    }

    [Fact]
    public void Build_UsesTheRealLengthOfFebruaryInALeapYear()
    {
        var periods = FixedAssetDepreciationSchedule.Build(
            costAmount: 60m, salvageValue: 0m, usefulLifeMonths: 2,
            depreciationStartDate: new DateOnly(2028, 2, 15), currencyScale: 0);

        periods[0].PeriodStart.Should().Be(new DateOnly(2028, 2, 1), "a full-month convention applies to the start month");
        periods[0].PeriodEnd.Should().Be(new DateOnly(2028, 2, 29));
        periods[1].PeriodEnd.Should().Be(new DateOnly(2028, 3, 31));
    }

    [Fact]
    public void Build_CrossesTheYearBoundaryWithoutGaps()
    {
        var periods = FixedAssetDepreciationSchedule.Build(
            costAmount: 300m, salvageValue: 0m, usefulLifeMonths: 3,
            depreciationStartDate: new DateOnly(2026, 11, 20), currencyScale: 2);

        periods.Select(period => period.PeriodEnd).Should().Equal(
            new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 31));
    }

    [Theory]
    [InlineData(0, 0, 12)]
    [InlineData(100, 100, 12)]
    [InlineData(100, 120, 12)]
    [InlineData(100, 0, 0)]
    [InlineData(100, 0, 601)]
    public void Build_RefusesImpossibleParameters(decimal cost, decimal salvage, int usefulLife)
    {
        var act = () => FixedAssetDepreciationSchedule.Build(
            cost, salvage, usefulLife, new DateOnly(2026, 1, 1), currencyScale: 2);

        act.Should().Throw<FinancePostingValidationException>();
    }

    [Fact]
    public void Build_RefusesAnAmountTooSmallToSpreadAtTheCompanyScale()
    {
        var act = () => FixedAssetDepreciationSchedule.Build(
            costAmount: 0.01m, salvageValue: 0m, usefulLifeMonths: 3,
            depreciationStartDate: new DateOnly(2026, 1, 1), currencyScale: 2);

        act.Should().Throw<FinancePostingValidationException>().WithMessage("*too small*");
    }

    [Fact]
    public void Build_RefusesAmountsOffTheCompanyScale()
    {
        var act = () => FixedAssetDepreciationSchedule.Build(
            costAmount: 10.005m, salvageValue: 0m, usefulLifeMonths: 2,
            depreciationStartDate: new DateOnly(2026, 1, 1), currencyScale: 2);

        act.Should().Throw<FinancePostingValidationException>().WithMessage("*2 decimal places*");
    }
}
