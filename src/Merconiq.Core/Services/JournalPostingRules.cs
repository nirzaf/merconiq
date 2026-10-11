using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Models;

namespace Merconiq.Core.Services;

/// <summary>
/// Pure finance-kernel invariants shared by the posting service, the API layer, and tests.
/// These rules never touch persistence so that balancing, scale, and hashing semantics can be
/// verified deterministically without a database.
/// </summary>
public static class JournalPostingRules
{
    /// <summary>A balanced journal needs at least one debit and one credit line.</summary>
    public const int MinimumLines = 2;

    /// <summary>Bounded request size so a single posting cannot exhaust statement limits.</summary>
    public const int MaximumLines = 500;

    /// <summary>Separator used by the canonical request hash; characters are forbidden in input fields.</summary>
    private const string FieldSeparator = "\u001f";

    /// <summary>
    /// Validates a posting request against the company's declared currency scale. Amounts must
    /// already be expressed at that scale; no implicit rounding or truncation happens here.
    /// </summary>
    /// <exception cref="FinancePostingValidationException">The request cannot post as supplied.</exception>
    public static void Validate(JournalPostingRequest request, int currencyScale)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(currencyScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currencyScale, 4);

        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        if (request.Lines is null || request.Lines.Count is < MinimumLines or > MaximumLines)
            throw new FinancePostingValidationException(
                $"A journal requires between {MinimumLines} and {MaximumLines} lines.");

        RequireBoundedText(request.SourceDocumentType, nameof(request.SourceDocumentType), 64);
        RequireBoundedText(request.SourceDocumentId, nameof(request.SourceDocumentId), 128);
        RequireBoundedText(request.SourceAction, nameof(request.SourceAction), 64);
        if (request.SourceVersion < 1)
            throw new FinancePostingValidationException("SourceVersion must be at least 1.");

        RequireBoundedText(request.Description, nameof(request.Description), 500);
        if (request.CurrencyCode?.Length != 3 || request.CurrencyCode != request.CurrencyCode.ToUpperInvariant())
            throw new FinancePostingValidationException("CurrencyCode must be a three-letter uppercase code.");

        var totalDebit = 0m;
        var totalCredit = 0m;
        foreach (var line in request.Lines)
        {
            if (line is null)
                throw new FinancePostingValidationException("Journal lines must not be null.");
            if (line.ChartOfAccountId <= 0)
                throw new FinancePostingValidationException("Every journal line needs a positive ChartOfAccountId.");
            if (line.Debit < 0 || line.Credit < 0)
                throw new FinancePostingValidationException("Journal line amounts must not be negative.");
            if ((line.Debit > 0) == (line.Credit > 0))
                throw new FinancePostingValidationException(
                    "Exactly one of debit or credit must be positive on every journal line.");

            AssertAtScale(line.Debit, currencyScale, "debit");
            AssertAtScale(line.Credit, currencyScale, "credit");

            if (line.Description is { } description)
                RequireBoundedText(description, "line description", 500);

            totalDebit += line.Debit;
            totalCredit += line.Credit;
        }

        if (totalDebit != totalCredit)
            throw new FinancePostingValidationException(
                $"Journal debits ({totalDebit}) must equal credits ({totalCredit}) exactly.");
        if (totalDebit <= 0)
            throw new FinancePostingValidationException("A journal must move a non-zero total value.");
    }

    /// <summary>
    /// Computes the deterministic request hash used to tell an idempotent replay from a
    /// conflicting retry of one committed source action.
    /// </summary>
    public static string ComputeRequestHash(JournalPostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = new StringBuilder();
        AppendField(canonical, request.CompanyId.ToString(CultureInfo.InvariantCulture));
        AppendField(canonical, request.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendField(canonical, request.CurrencyCode);
        AppendField(canonical, request.Description);
        AppendField(canonical, request.SourceDocumentType);
        AppendField(canonical, request.SourceDocumentId);
        AppendField(canonical, request.SourceAction);
        AppendField(canonical, request.SourceVersion.ToString(CultureInfo.InvariantCulture));
        // Appended only when targeted, so hashes of regular journals stay identical to committed history.
        if (request.FiscalPeriodId is int targetPeriodId)
            AppendField(canonical, "adjustment-period:" + targetPeriodId.ToString(CultureInfo.InvariantCulture));
        foreach (var line in request.Lines)
        {
            AppendField(canonical, line.ChartOfAccountId.ToString(CultureInfo.InvariantCulture));
            AppendField(canonical, line.Debit.ToString("0.########", CultureInfo.InvariantCulture));
            AppendField(canonical, line.Credit.ToString("0.########", CultureInfo.InvariantCulture));
            AppendField(canonical, line.Description ?? string.Empty);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Builds the source-linked source-document identifier for one journal reversal.</summary>
    public static string ReversalSourceDocumentId(int journalEntryId) =>
        journalEntryId.ToString(CultureInfo.InvariantCulture);

    private static void AssertAtScale(decimal amount, int currencyScale, string side)
    {
        if (amount != Math.Round(amount, currencyScale, MidpointRounding.ToEven))
            throw new FinancePostingValidationException(
                $"Journal line {side} amounts must already be rounded to {currencyScale} decimal places.");
    }

    private static void RequireBoundedText(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FinancePostingValidationException($"{fieldName} must not be blank.");
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new FinancePostingValidationException($"{fieldName} must be at most {maxLength} characters.");
    }

    private static void AppendField(StringBuilder builder, string value) =>
        builder.Append(value.Replace(FieldSeparator, " ", StringComparison.Ordinal)).Append(FieldSeparator);
}
