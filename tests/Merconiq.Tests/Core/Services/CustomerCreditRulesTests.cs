using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>Credit-limit arithmetic: limits are non-negative and at scale; exceeding is strictly above the limit.</summary>
public class CustomerCreditRulesTests
{
    [Fact]
    public void ValidateCreditLimit_AllowsNoLimitAndWholeScaledAmounts()
    {
        var act = () =>
        {
            CustomerCreditRules.ValidateCreditLimit(null, 2);
            CustomerCreditRules.ValidateCreditLimit(0m, 2);
            CustomerCreditRules.ValidateCreditLimit(2500.50m, 2);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateCreditLimit_RejectsNegativeAndOffScaleLimits()
    {
        var negative = () => CustomerCreditRules.ValidateCreditLimit(-1m, 2);
        var offScale = () => CustomerCreditRules.ValidateCreditLimit(10.005m, 2);

        negative.Should().Throw<FinancePostingValidationException>().WithMessage("*cannot be negative*");
        offScale.Should().Throw<FinancePostingValidationException>().WithMessage("*2 decimal places*");
    }

    [Fact]
    public void WouldExceed_IsStrictlyAboveTheLimitAndNeverTrueWithoutOne()
    {
        CustomerCreditRules.WouldExceed(1000m, 1000m).Should().BeFalse("exactly at the limit is allowed");
        CustomerCreditRules.WouldExceed(1000m, 1000.01m).Should().BeTrue();
        CustomerCreditRules.WouldExceed(null, 1_000_000m).Should().BeFalse("no limit means no cap");
    }

    [Fact]
    public void Available_IsTheHeadroomOrNullWhenUnlimited()
    {
        CustomerCreditRules.Available(1000m, 250m).Should().Be(750m);
        CustomerCreditRules.Available(1000m, 1200m).Should().Be(-200m, "an overdrawn customer shows negative headroom");
        CustomerCreditRules.Available(null, 250m).Should().BeNull();
    }
}
