using System.Globalization;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Till approval for held shift closes. Approval runs the same over/short posting an immediate close uses, under
/// the shift lock, so a held variance posts exactly once and only after a reviewer decides. Every decision is
/// recorded on the shift and captured by the audit log.
/// </summary>
public sealed class TillApprovalService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IAccountPostingPolicy postingPolicy,
    IJournalPostingService journalPosting,
    ILogger<TillApprovalService> logger) : ITillApprovalService
{
    /// <inheritdoc />
    public async Task<TillApprovalResult> ApproveCloseAsync(
        int companyId,
        int shiftId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        TillApprovalResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-shift:{companyId}", cancellationToken);

            var shift = await FindShiftAsync(companyId, shiftId, cancellationToken);
            RequirePending(shift);

            var overShort = shift.OverShortValue ?? 0m;
            string? journalNumber = null;
            if (overShort != 0m)
            {
                var company = await context.Companies.SingleAsync(candidate => candidate.Id == companyId, cancellationToken);
                var cashAccount = await postingPolicy.ResolveAsync(companyId, AccountPostingRole.CashOnHand, cancellationToken);
                var overShortAccount = await postingPolicy.ResolveAsync(companyId, AccountPostingRole.CashOverShort, cancellationToken);
                var posted = await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        companyId,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        company.BaseCurrency,
                        $"Cash over/short for shift on register {shift.Register.Name}",
                        PosService.ShiftDocumentType,
                        shift.Id.ToString(CultureInfo.InvariantCulture),
                        "Close",
                        1,
                        PosService.BuildOverShortLines(overShortAccount.ChartOfAccountId, cashAccount.ChartOfAccountId, overShort)),
                    actor,
                    cancellationToken);
                journalNumber = posted.JournalNumber;
            }

            shift.Status = PosShiftStatus.Closed;
            shift.CloseApprovedBy = actor.Trim();
            shift.CloseApprovedAtUtc = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = ToResult(shift, journalNumber);
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Approved held close of POS shift {ShiftId} for company {CompanyId}", shiftId, companyId);
        return result;
    }

    /// <inheritdoc />
    public async Task<TillApprovalResult> RejectCloseAsync(
        int companyId,
        int shiftId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        TillApprovalResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-shift:{companyId}", cancellationToken);

            var shift = await FindShiftAsync(companyId, shiftId, cancellationToken);
            RequirePending(shift);

            // The shift reopens for sales and a recount; the held figures stay visible in the audit log.
            shift.Status = PosShiftStatus.Open;
            shift.ClosedAtUtc = null;
            shift.ClosedBy = null;
            shift.CountedCash = null;
            shift.ExpectedCash = null;
            shift.OverShortValue = null;
            shift.CloseRejectedBy = actor.Trim();
            shift.CloseRejectedAtUtc = DateTime.UtcNow;
            shift.CloseRejectionReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = ToResult(shift, journalNumber: null);
        }, cancellationToken: cancellationToken);

        logger.LogWarning(
            "Rejected held close of POS shift {ShiftId} for company {CompanyId}; reason recorded", shiftId, companyId);
        return result;
    }

    /// <inheritdoc />
    public async Task<PosRegister> SetVarianceThresholdAsync(
        int companyId,
        int registerId,
        decimal? threshold,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        PosRegister updated = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Shares the shift lock so a threshold change cannot race a shift close that reads it.
            await unitOfWork.AcquireTenantOperationLockAsync($"pos-shift:{companyId}", cancellationToken);

            var company = await context.Companies.SingleOrDefaultAsync(
                    candidate => candidate.Id == companyId && candidate.IsActive,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Active company not found.");
            if (threshold is { } value)
            {
                if (value < 0m)
                    throw new FinancePostingValidationException("A variance approval threshold cannot be negative.");
                if (value != Math.Round(value, company.CurrencyScale ?? 2, MidpointRounding.ToEven))
                    throw new FinancePostingValidationException(
                        $"The variance approval threshold must be expressed at {company.CurrencyScale ?? 2} decimal places.");
            }

            var register = await context.PosRegisters.SingleOrDefaultAsync(
                    candidate => candidate.Id == registerId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Register not found in this company.");
            register.VarianceApprovalThreshold = threshold;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            updated = register;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Set variance approval threshold of register {RegisterId} to {Threshold} for company {CompanyId}",
            registerId, threshold?.ToString(CultureInfo.InvariantCulture) ?? "none", companyId);
        return updated;
    }

    private async Task<PosShift> FindShiftAsync(int companyId, int shiftId, CancellationToken cancellationToken) =>
        await context.PosShifts
            .Include(candidate => candidate.Register)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == shiftId && candidate.CompanyId == companyId,
                cancellationToken)
        ?? throw new FinancePostingValidationException("Shift not found in this company.");

    private static void RequirePending(PosShift shift)
    {
        if (shift.Status != PosShiftStatus.PendingApproval)
            throw new FinancePostingConflictException(
                $"The shift is {shift.Status.ToString().ToLowerInvariant()}; only a close awaiting approval can be decided.");
    }

    private static TillApprovalResult ToResult(PosShift shift, string? journalNumber) => new(
        shift.Id,
        shift.Status,
        shift.ExpectedCash ?? 0m,
        shift.CountedCash ?? 0m,
        shift.OverShortValue ?? 0m,
        journalNumber,
        shift.ClosedBy,
        shift.CloseApprovedBy,
        shift.CloseApprovedAtUtc,
        shift.CloseRejectedBy,
        shift.CloseRejectionReason);

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
