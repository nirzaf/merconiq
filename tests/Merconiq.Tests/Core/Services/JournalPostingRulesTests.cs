using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Pure finance-kernel invariants: balancing, single-sided lines, currency scale, and the
/// deterministic request hash that separates idempotent replays from conflicting retries.
/// </summary>
public class JournalPostingRulesTests
{
    private static readonly DateOnly PostingDate = new(2026, 1, 15);

    private static JournalPostingRequest BalancedRequest(
        decimal debit = 100m,
        decimal credit = 100m) => new(
        1,
        PostingDate,
        "USD",
        "Opening balance",
        "OpeningBalance",
        "doc-1",
        "Post",
        1,
        [
            new JournalLineInput(10, "Bank", debit, 0m),
            new JournalLineInput(20, "Equity", 0m, credit)
        ]);

    [Fact]
    public void Validate_BalancedTwoLineRequest_Passes()
    {
        var act = () => JournalPostingRules.Validate(BalancedRequest(), 2);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_UnbalancedTotals_AreRejected()
    {
        var request = BalancedRequest(debit: 100m, credit: 99.99m);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>()
            .WithMessage("*must equal credits*exactly*");
    }

    [Fact]
    public void Validate_BothSidesPositiveOnOneLine_AreRejected()
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "Both sides", "OpeningBalance", "doc-1", "Post", 1,
            [
                new JournalLineInput(10, null, 100m, 50m),
                new JournalLineInput(20, null, 0m, 50m)
            ]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>()
            .WithMessage("*Exactly one of debit or credit*");
    }

    [Fact]
    public void Validate_ZeroAmountLines_AreRejected()
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "Zero line", "OpeningBalance", "doc-1", "Post", 1,
            [
                new JournalLineInput(10, null, 100m, 0m),
                new JournalLineInput(20, null, 0m, 100m),
                new JournalLineInput(30, null, 0m, 0m)
            ]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>()
            .WithMessage("*Exactly one of debit or credit*");
    }

    [Theory]
    [InlineData(-0.01)]
    public void Validate_NegativeAmounts_AreRejected(decimal amount)
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "Negative", "OpeningBalance", "doc-1", "Post", 1,
            [
                new JournalLineInput(10, null, amount, 0m),
                new JournalLineInput(20, null, 0m, amount)
            ]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>()
            .WithMessage("*must not be negative*");
    }

    [Fact]
    public void Validate_AmountBeyondCurrencyScale_IsRejectedWithoutRounding()
    {
        var request = BalancedRequest(debit: 100.005m, credit: 100.005m);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>()
            .WithMessage("*rounded to 2 decimal places*");
    }

    [Fact]
    public void Validate_AmountAtCompanyScale_Passes()
    {
        var request = BalancedRequest(debit: 0.01m, credit: 0.01m);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_SingleLineJournal_IsRejected()
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "One line", "OpeningBalance", "doc-1", "Post", 1,
            [new JournalLineInput(10, null, 100m, 0m)]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>().WithMessage("*between 2 and 500 lines*");
    }

    [Fact]
    public void Validate_AllZeroLines_AreRejectedBySingleSideRule()
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "Zero total", "OpeningBalance", "doc-1", "Post", 1,
            [
                new JournalLineInput(10, null, 0m, 0m),
                new JournalLineInput(20, null, 0m, 0m)
            ]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Validate_BlankSourceFields_AreRejected(string? sourceId)
    {
        var request = new JournalPostingRequest(
            1, PostingDate, "USD", "Blank source", "OpeningBalance", sourceId!, "Post", 1,
            [
                new JournalLineInput(10, null, 100m, 0m),
                new JournalLineInput(20, null, 0m, 100m)
            ]);
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>().WithMessage("*SourceDocumentId*");
    }

    [Fact]
    public void Validate_SourceVersionBelowOne_IsRejected()
    {
        var request = BalancedRequest() with { SourceVersion = 0 };
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>().WithMessage("*SourceVersion*");
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Validate_InvalidCurrencyCode_IsRejected(string currency)
    {
        var request = BalancedRequest() with { CurrencyCode = currency };
        var act = () => JournalPostingRules.Validate(request, 2);
        act.Should().Throw<FinancePostingValidationException>().WithMessage("*CurrencyCode*");
    }

    [Fact]
    public void ComputeRequestHash_IsDeterministicForIdenticalPayloads()
    {
        var first = JournalPostingRules.ComputeRequestHash(BalancedRequest());
        var second = JournalPostingRules.ComputeRequestHash(BalancedRequest());
        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeRequestHash_ChangesWhenAnyAmountChanges()
    {
        var baseline = JournalPostingRules.ComputeRequestHash(BalancedRequest());
        var changed = JournalPostingRules.ComputeRequestHash(BalancedRequest(debit: 100.01m, credit: 100.01m));
        changed.Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeRequestHash_ChangesWhenSourceIdentityChanges()
    {
        var baseline = JournalPostingRules.ComputeRequestHash(BalancedRequest());
        var differentAction = JournalPostingRules.ComputeRequestHash(BalancedRequest() with { SourceAction = "Adjust" });
        var differentVersion = JournalPostingRules.ComputeRequestHash(BalancedRequest() with { SourceVersion = 2 });
        var differentSource = JournalPostingRules.ComputeRequestHash(BalancedRequest() with { SourceDocumentId = "doc-2" });

        differentAction.Should().NotBe(baseline);
        differentVersion.Should().NotBe(baseline);
        differentSource.Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeRequestHash_IgnoresLineOrderOnlyWhenOrderActuallyDiffers()
    {
        // The hash captures the supplied line order so a retried request with reordered lines
        // and per-line semantics is detectable rather than silently treated as identical.
        var request = BalancedRequest();
        var reordered = request with
        {
            Lines = request.Lines.Reverse().ToArray()
        };
        JournalPostingRules.ComputeRequestHash(request).Should()
            .NotBe(JournalPostingRules.ComputeRequestHash(reordered));
    }
}
