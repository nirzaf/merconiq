using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One expense on a new claim; the account must be an active, non-group chart account of the company.</summary>
public sealed record ExpenseClaimLineInput(int ExpenseChartOfAccountId, string Description, decimal Amount);

/// <summary>Creates one draft expense claim; the claim number is allocated by the company's document sequence.</summary>
public sealed record CreateExpenseClaimRequest(
    int CompanyId,
    string ClaimantName,
    DateOnly ClaimDate,
    string Description,
    IReadOnlyList<ExpenseClaimLineInput> Lines);

/// <summary>A claim with its lines.</summary>
public sealed record ExpenseClaimView(ExpenseClaim Claim, IReadOnlyList<ExpenseClaimLine> Lines);
