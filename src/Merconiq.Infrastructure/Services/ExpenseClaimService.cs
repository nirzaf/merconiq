using System.Globalization;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Expense-claim lifecycle over the finance kernel. Approval posts each expense line against its chosen account
/// and credits the employee payable; payment debits that payable and credits the bank account. Each operation
/// runs in one transaction with its status change, and the kernel's source keys make each journal post once.
/// </summary>
public sealed class ExpenseClaimService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IJournalPostingService journal,
    IAccountPostingPolicy postingPolicy,
    IDocumentNumberService documentNumbers,
    ILogger<ExpenseClaimService> logger) : IExpenseClaimService
{
    /// <summary>Journal source document family used by every expense-claim posting.</summary>
    public const string SourceDocumentType = "ExpenseClaim";

    private const string ClaimDocumentType = "ExpenseClaim";
    private const string ClaimNumberPrefix = "EC";
    private const string ApproveAction = "Approve";
    private const string PayAction = "Pay";

    /// <inheritdoc />
    public async Task<ExpenseClaim> CreateAsync(
        CreateExpenseClaimRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(request.CompanyId);
        var claimantName = ExpenseClaimRules.ValidateHeader(request.ClaimantName, request.Description);

        ExpenseClaim created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var company = await RequireActiveCompanyAsync(request.CompanyId, cancellationToken);
            var total = ExpenseClaimRules.ValidateLines(request.Lines, company.CurrencyScale ?? 2);

            var accountIds = request.Lines.Select(line => line.ExpenseChartOfAccountId).Distinct().ToList();
            var accounts = await context.ChartOfAccounts
                .Where(account => account.CompanyId == request.CompanyId && accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, cancellationToken);
            foreach (var accountId in accountIds)
            {
                if (!accounts.TryGetValue(accountId, out var account))
                    throw new FinancePostingValidationException($"Expense account {accountId} does not exist in this company.");
                if (!account.IsActive)
                    throw new FinancePostingValidationException($"Expense account '{account.AccountCode}' is inactive.");
                if (account.IsGroupAccount)
                    throw new FinancePostingValidationException(
                        $"Expense account '{account.AccountCode}' is a group account and cannot receive claim lines.");
            }

            var claimNumber = await documentNumbers.AllocateAsync(
                request.CompanyId, ClaimDocumentType, request.ClaimDate.Year, ClaimNumberPrefix, cancellationToken);
            var claim = new ExpenseClaim
            {
                CompanyId = request.CompanyId,
                ClaimNumber = claimNumber,
                ClaimantName = claimantName,
                ClaimDate = request.ClaimDate,
                Description = request.Description.Trim(),
                Status = ExpenseClaimStatus.Draft,
                TotalAmount = total,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUser = actor.Trim(),
                Lines = request.Lines
                    .Select((line, index) => new ExpenseClaimLine
                    {
                        CompanyId = request.CompanyId,
                        LineNumber = index + 1,
                        ExpenseChartOfAccountId = line.ExpenseChartOfAccountId,
                        Description = line.Description.Trim(),
                        Amount = line.Amount
                    })
                    .ToList()
            };
            context.ExpenseClaims.Add(claim);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = claim;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Created expense claim {ClaimNumber} for company {CompanyId}", created.ClaimNumber, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpenseClaim>> ListAsync(int companyId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.ExpenseClaims
            .Where(claim => claim.CompanyId == companyId)
            .OrderByDescending(claim => claim.ClaimDate)
            .ThenByDescending(claim => claim.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ExpenseClaimView?> GetAsync(int companyId, int claimId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var claim = await context.ExpenseClaims.SingleOrDefaultAsync(
            candidate => candidate.Id == claimId && candidate.CompanyId == companyId,
            cancellationToken);
        if (claim is null)
            return null;

        var lines = await context.ExpenseClaimLines
            .Where(line => line.CompanyId == companyId && line.ExpenseClaimId == claim.Id)
            .OrderBy(line => line.LineNumber)
            .ToListAsync(cancellationToken);
        return new ExpenseClaimView(claim, lines);
    }

    /// <inheritdoc />
    public async Task<ExpenseClaim> SubmitAsync(int companyId, int claimId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        ExpenseClaim submitted = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var claim = await FindClaimAsync(companyId, claimId, cancellationToken);
            RequireStatus(claim, ExpenseClaimStatus.Draft, "submitted");
            claim.Status = ExpenseClaimStatus.Submitted;
            claim.SubmittedAtUtc = DateTime.UtcNow;
            claim.SubmittedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            submitted = claim;
        }, cancellationToken: cancellationToken);
        return submitted;
    }

    /// <inheritdoc />
    public async Task<ExpenseClaim> ApproveAsync(int companyId, int claimId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        ExpenseClaim approved = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var company = await RequireActiveCompanyAsync(companyId, cancellationToken);
            var claim = await FindClaimAsync(companyId, claimId, cancellationToken);
            RequireStatus(claim, ExpenseClaimStatus.Submitted, "approved");

            var lines = await context.ExpenseClaimLines
                .Where(line => line.CompanyId == companyId && line.ExpenseClaimId == claim.Id)
                .OrderBy(line => line.LineNumber)
                .ToListAsync(cancellationToken);
            var payable = await postingPolicy.ResolveAsync(companyId, AccountPostingRole.EmployeeExpensesPayable, cancellationToken);

            var journalLines = new List<JournalLineInput>(lines.Count + 1);
            journalLines.AddRange(lines.Select(line =>
                new JournalLineInput(line.ExpenseChartOfAccountId, line.Description, line.Amount, 0m)));
            journalLines.Add(new JournalLineInput(payable.ChartOfAccountId, $"Payable to {claim.ClaimantName}", 0m, claim.TotalAmount));

            var result = await journal.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    claim.ClaimDate,
                    company.BaseCurrency,
                    $"Expense claim {claim.ClaimNumber}: {claim.Description}",
                    SourceDocumentType,
                    claim.Id.ToString(CultureInfo.InvariantCulture),
                    ApproveAction,
                    1,
                    journalLines),
                actor,
                cancellationToken);

            claim.Status = ExpenseClaimStatus.Approved;
            claim.ApprovalJournalEntryId = result.JournalEntryId;
            claim.ApprovedAtUtc = DateTime.UtcNow;
            claim.ApprovedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            approved = claim;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Approved expense claim {ClaimNumber} for company {CompanyId}", approved.ClaimNumber, companyId);
        return approved;
    }

    /// <inheritdoc />
    public async Task<ExpenseClaim> RejectAsync(
        int companyId,
        int claimId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var reasonText = RequireReason(reason);

        ExpenseClaim rejected = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var claim = await FindClaimAsync(companyId, claimId, cancellationToken);
            RequireStatus(claim, ExpenseClaimStatus.Submitted, "rejected");
            claim.Status = ExpenseClaimStatus.Rejected;
            claim.RejectedAtUtc = DateTime.UtcNow;
            claim.RejectedBy = actor.Trim();
            claim.RejectionReason = reasonText;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            rejected = claim;
        }, cancellationToken: cancellationToken);
        return rejected;
    }

    /// <inheritdoc />
    public async Task<ExpenseClaim> PayAsync(
        int companyId,
        int claimId,
        int paymentChartOfAccountId,
        DateOnly paymentDate,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        ExpenseClaim paid = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var company = await RequireActiveCompanyAsync(companyId, cancellationToken);
            var claim = await FindClaimAsync(companyId, claimId, cancellationToken);
            RequireStatus(claim, ExpenseClaimStatus.Approved, "paid");

            var bank = await context.ChartOfAccounts.SingleOrDefaultAsync(
                    account => account.Id == paymentChartOfAccountId && account.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Payment account not found in this company.");
            if (!bank.IsActive || bank.IsGroupAccount)
                throw new FinancePostingValidationException(
                    $"Payment account '{bank.AccountCode}' must be an active, non-group account.");
            var payable = await postingPolicy.ResolveAsync(companyId, AccountPostingRole.EmployeeExpensesPayable, cancellationToken);

            var result = await journal.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    paymentDate,
                    company.BaseCurrency,
                    $"Payment of expense claim {claim.ClaimNumber} to {claim.ClaimantName}",
                    SourceDocumentType,
                    claim.Id.ToString(CultureInfo.InvariantCulture),
                    PayAction,
                    1,
                    [
                        new JournalLineInput(payable.ChartOfAccountId, claim.ClaimantName, claim.TotalAmount, 0m),
                        new JournalLineInput(bank.Id, claim.ClaimantName, 0m, claim.TotalAmount)
                    ]),
                actor,
                cancellationToken);

            claim.Status = ExpenseClaimStatus.Paid;
            claim.PaymentJournalEntryId = result.JournalEntryId;
            claim.PaymentDate = paymentDate;
            claim.PaidAtUtc = DateTime.UtcNow;
            claim.PaidBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            paid = claim;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Paid expense claim {ClaimNumber} for company {CompanyId}", paid.ClaimNumber, companyId);
        return paid;
    }

    private async Task<Company> RequireActiveCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        await context.Companies.SingleOrDefaultAsync(
            candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private async Task<ExpenseClaim> FindClaimAsync(int companyId, int claimId, CancellationToken cancellationToken) =>
        await context.ExpenseClaims.SingleOrDefaultAsync(
            candidate => candidate.Id == claimId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Expense claim not found in this company.");

    private static void RequireStatus(ExpenseClaim claim, ExpenseClaimStatus required, string target)
    {
        if (claim.Status != required)
            throw new FinancePostingConflictException(
                $"Claim {claim.ClaimNumber} is {claim.Status.ToString().ToLowerInvariant()} and cannot be {target}.");
    }

    private static void RequirePositiveCompany(int companyId)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reason of 1 to 500 characters is required.");
        return trimmed;
    }
}
