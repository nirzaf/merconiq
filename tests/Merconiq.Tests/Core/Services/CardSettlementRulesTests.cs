using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>Settlement arithmetic: the net paid out is gross less fee, and a batch never pays out more than its gross.</summary>
public class CardSettlementRulesTests
{
    [Fact]
    public void ValidateBatch_ReturnsTheNetPaidOut()
    {
        CardSettlementRules.ValidateBatch(grossAmount: 30m, feeAmount: 0.5m, currencyScale: 2).Should().Be(29.5m);
    }

    [Fact]
    public void ValidateBatch_AllowsAFeeFreeBatchButRejectsNegativeOrOversizedFees()
    {
        CardSettlementRules.ValidateBatch(100m, 0m, 2).Should().Be(100m);

        var negative = () => CardSettlementRules.ValidateBatch(100m, -1m, 2);
        var oversized = () => CardSettlementRules.ValidateBatch(100m, 100.01m, 2);

        negative.Should().Throw<FinancePostingValidationException>();
        oversized.Should().Throw<FinancePostingValidationException>();
    }

    [Fact]
    public void ValidateBatch_RejectsEmptyBatchesAndOffScaleAmounts()
    {
        var empty = () => CardSettlementRules.ValidateBatch(0m, 0m, 2);
        var offScale = () => CardSettlementRules.ValidateBatch(10.005m, 0m, 2);

        empty.Should().Throw<FinancePostingValidationException>().WithMessage("*positive gross*");
        offScale.Should().Throw<FinancePostingValidationException>().WithMessage("*2 decimal places*");
    }

    [Fact]
    public void RemainingGross_IsWhatTheBatchHasNotYetSettled()
    {
        CardSettlementRules.RemainingGross(30m, 12m).Should().Be(18m);
        CardSettlementRules.RemainingGross(30m, 30m).Should().Be(0m);
    }
}
