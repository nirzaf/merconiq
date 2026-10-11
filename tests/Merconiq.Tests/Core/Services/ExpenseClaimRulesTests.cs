using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>Claim validation: a named claimant, bounded text, and positive amounts at the company's scale.</summary>
public class ExpenseClaimRulesTests
{
    [Fact]
    public void ValidateHeader_TrimsTheClaimantAndRequiresText()
    {
        ExpenseClaimRules.ValidateHeader("  Nadia Perera  ", "Client visit").Should().Be("Nadia Perera");

        var blankName = () => ExpenseClaimRules.ValidateHeader("   ", "Client visit");
        var blankDescription = () => ExpenseClaimRules.ValidateHeader("Nadia", " ");

        blankName.Should().Throw<FinancePostingValidationException>();
        blankDescription.Should().Throw<FinancePostingValidationException>();
    }

    [Fact]
    public void ValidateLines_ReturnsTheTotalOfPositiveScaledAmounts()
    {
        var total = ExpenseClaimRules.ValidateLines(
            [new ExpenseClaimLineInput(1, "Taxi", 40m), new ExpenseClaimLineInput(2, "Lunch", 10.25m)], currencyScale: 2);

        total.Should().Be(50.25m);
    }

    [Fact]
    public void ValidateLines_RejectsEmptyZeroNegativeAndOffScaleLines()
    {
        var empty = () => ExpenseClaimRules.ValidateLines([], currencyScale: 2);
        var zero = () => ExpenseClaimRules.ValidateLines([new ExpenseClaimLineInput(1, "Taxi", 0m)], currencyScale: 2);
        var negative = () => ExpenseClaimRules.ValidateLines([new ExpenseClaimLineInput(1, "Taxi", -5m)], currencyScale: 2);
        var offScale = () => ExpenseClaimRules.ValidateLines([new ExpenseClaimLineInput(1, "Taxi", 1.005m)], currencyScale: 2);

        empty.Should().Throw<FinancePostingValidationException>();
        zero.Should().Throw<FinancePostingValidationException>();
        negative.Should().Throw<FinancePostingValidationException>();
        offScale.Should().Throw<FinancePostingValidationException>().WithMessage("*2 decimal places*");
    }
}
