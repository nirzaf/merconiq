using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;

namespace Merconiq.Core.Services;

/// <summary>
/// Pure bank-reconciliation arithmetic. A statement must foot (opening plus movements equals closing)
/// before it is accepted, and the reconciliation difference is computed from the same four figures the
/// report shows, so a reader can check every number by hand.
/// </summary>
public static class BankReconciliationRules
{
    /// <summary>Upper bound on lines in one imported statement, so one import stays a bounded transaction.</summary>
    public const int MaximumStatementLines = 2000;

    /// <summary>Upper bound on unmatched ledger items returned in one report; totals always cover all items.</summary>
    public const int MaximumListedLedgerItems = 200;

    private const int MaximumDescriptionLength = 500;

    /// <summary>Rejects statements whose lines do not foot, or whose amounts are not at the company's scale.</summary>
    public static void ValidateStatement(
        decimal openingBalance,
        decimal closingBalance,
        IReadOnlyList<BankStatementLineInput> lines,
        int currencyScale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count > MaximumStatementLines)
            throw new FinancePostingValidationException(
                $"A bank statement may contain at most {MaximumStatementLines} lines.");

        AssertAtScale(openingBalance, currencyScale, "opening balance");
        AssertAtScale(closingBalance, currencyScale, "closing balance");

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var position = index + 1;
            AssertAtScale(line.Amount, currencyScale, $"line {position} amount");
            if (line.Amount == 0m)
                throw new FinancePostingValidationException($"Statement line {position} must carry a non-zero amount.");
            var description = line.Description?.Trim() ?? string.Empty;
            if (description.Length is 0 or > MaximumDescriptionLength)
                throw new FinancePostingValidationException(
                    $"Statement line {position} needs a description of 1 to {MaximumDescriptionLength} characters.");
        }

        var movement = lines.Sum(line => line.Amount);
        if (openingBalance + movement != closingBalance)
        {
            throw new FinancePostingValidationException(
                $"Opening balance {openingBalance} plus statement movements {movement} must equal the closing balance {closingBalance}.");
        }
    }

    /// <summary>
    /// Adjusted bank balance minus adjusted book balance. Zero means the unexplained differences on the
    /// two sides cancel exactly.
    /// </summary>
    public static decimal Difference(
        decimal closingBalance,
        decimal ledgerBalance,
        decimal unmatchedLedgerAmount,
        decimal unmatchedStatementAmount) =>
        (closingBalance + unmatchedLedgerAmount) - (ledgerBalance + unmatchedStatementAmount);

    /// <summary>
    /// A statement is reconciled when the adjusted balances agree and every bank line is explained by a
    /// ledger line. Unmatched ledger lines are timing items that may legitimately remain open.
    /// </summary>
    public static bool IsReconciled(decimal difference, int unmatchedStatementLineCount) =>
        difference == 0m && unmatchedStatementLineCount == 0;

    private static void AssertAtScale(decimal amount, int currencyScale, string label)
    {
        if (amount != Math.Round(amount, currencyScale, MidpointRounding.ToEven))
            throw new FinancePostingValidationException(
                $"The {label} must be expressed at {currencyScale} decimal places.");
    }
}
