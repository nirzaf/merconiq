namespace Merconiq.Core.Entities;

/// <summary>
/// Expense-claim lifecycle. Approval posts the claim as a payable; payment settles that payable from a bank
/// account. A rejected claim is final and must be re-entered as a new claim.
/// </summary>
public enum ExpenseClaimStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4
}
