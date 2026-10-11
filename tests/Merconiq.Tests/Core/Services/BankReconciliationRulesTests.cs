using FluentAssertions;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Pure bank-statement rules: a statement must foot exactly before it is accepted, amounts must sit at the
/// company's currency scale, and the reconciliation difference cancels the timing items on both sides.
/// </summary>
public class BankReconciliationRulesTests
{
    [Fact]
    public void ValidateStatement_AcceptsAStatementThatFoots()
    {
        var lines = new[]
        {
            new BankStatementLineInput(new DateOnly(2026, 1, 2), "Deposit", null, 500m),
            new BankStatementLineInput(new DateOnly(2026, 1, 9), "Rent", "R-1", -80m)
        };

        var act = () => BankReconciliationRules.ValidateStatement(100m, 520m, lines, currencyScale: 2);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateStatement_RejectsStatementsThatDoNotFoot()
    {
        var lines = new[] { new BankStatementLineInput(new DateOnly(2026, 1, 2), "Deposit", null, 500m) };

        var act = () => BankReconciliationRules.ValidateStatement(0m, 499m, lines, currencyScale: 2);

        act.Should().Throw<FinancePostingValidationException>().WithMessage("*must equal the closing balance*");
    }

    [Fact]
    public void ValidateStatement_RejectsAmountsOffTheCompanyScale()
    {
        var lines = new[] { new BankStatementLineInput(new DateOnly(2026, 1, 2), "Fee", null, 1.005m) };

        var act = () => BankReconciliationRules.ValidateStatement(0m, 1.005m, lines, currencyScale: 2);

        act.Should().Throw<FinancePostingValidationException>().WithMessage("*2 decimal places*");
    }

    [Fact]
    public void ValidateStatement_RejectsZeroAmountsAndBlankDescriptions()
    {
        var zero = () => BankReconciliationRules.ValidateStatement(
            0m, 0m, [new BankStatementLineInput(new DateOnly(2026, 1, 2), "Noise", null, 0m)], 2);
        var blank = () => BankReconciliationRules.ValidateStatement(
            0m, 5m, [new BankStatementLineInput(new DateOnly(2026, 1, 2), "   ", null, 5m)], 2);

        zero.Should().Throw<FinancePostingValidationException>().WithMessage("*non-zero*");
        blank.Should().Throw<FinancePostingValidationException>().WithMessage("*description*");
    }

    [Fact]
    public void ValidateStatement_AcceptsAnEmptyStatementWhenBalancesAgree()
    {
        var act = () => BankReconciliationRules.ValidateStatement(250m, 250m, [], currencyScale: 2);

        act.Should().NotThrow();
    }

    [Fact]
    public void Difference_CancelsTimingItemsOnBothSides()
    {
        // Ledger has an uncleared deposit of 300; the statement has not yet shown it.
        var depositInTransit = BankReconciliationRules.Difference(
            closingBalance: 420m, ledgerBalance: 720m, unmatchedLedgerAmount: 300m, unmatchedStatementAmount: 0m);

        // Statement shows a bank fee of 15 that the ledger does not have yet.
        var bankFee = BankReconciliationRules.Difference(
            closingBalance: 705m, ledgerBalance: 720m, unmatchedLedgerAmount: 0m, unmatchedStatementAmount: -15m);

        depositInTransit.Should().Be(0m);
        bankFee.Should().Be(0m);
    }

    [Fact]
    public void Difference_ReportsAnUnexplainedGap()
    {
        var difference = BankReconciliationRules.Difference(
            closingBalance: 700m, ledgerBalance: 720m, unmatchedLedgerAmount: 0m, unmatchedStatementAmount: 0m);

        difference.Should().Be(-20m);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(5, 0, false)]
    public void IsReconciled_RequiresAgreeingBalancesAndNoUnexplainedBankLines(
        decimal difference, int unmatchedStatementLines, bool expected)
    {
        BankReconciliationRules.IsReconciled(difference, unmatchedStatementLines).Should().Be(expected);
    }
}
