using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;

namespace Merconiq.Core.Services;

/// <summary>Claim validation: named claimant, bounded text, and positive line amounts at the company's scale.</summary>
public static class ExpenseClaimRules
{
    public const int MaximumLines = 200;
    private const int MaximumNameLength = 200;
    private const int MaximumTextLength = 500;

    /// <summary>Validates claim header text and returns the trimmed claimant name.</summary>
    public static string ValidateHeader(string claimantName, string description)
    {
        var name = claimantName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaximumNameLength)
            throw new FinancePostingValidationException("A claimant name of 1 to 200 characters is required.");
        var text = description?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaximumTextLength)
            throw new FinancePostingValidationException("A claim description of 1 to 500 characters is required.");
        return name;
    }

    /// <summary>Validates lines and returns their total; every line must be a positive amount at the company scale.</summary>
    public static decimal ValidateLines(IReadOnlyList<ExpenseClaimLineInput> lines, int currencyScale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A claim needs between 1 and {MaximumLines} lines.");

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var position = index + 1;
            if (line.Amount <= 0m)
                throw new FinancePostingValidationException($"Claim line {position} must carry a positive amount.");
            if (line.Amount != Math.Round(line.Amount, currencyScale, MidpointRounding.ToEven))
                throw new FinancePostingValidationException(
                    $"Claim line {position} must be expressed at {currencyScale} decimal places.");
            var text = line.Description?.Trim() ?? string.Empty;
            if (text.Length is 0 or > MaximumTextLength)
                throw new FinancePostingValidationException(
                    $"Claim line {position} needs a description of 1 to 500 characters.");
        }

        return lines.Sum(line => line.Amount);
    }
}
