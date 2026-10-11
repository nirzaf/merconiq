using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>
/// Employee expense claims. Drafting and submitting need Edit; approving or rejecting needs Approve; paying
/// needs Post. Approval and payment post balanced journals through the finance kernel.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/expense-claims")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class ExpenseClaimsController(
    IExpenseClaimService claims,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List the company's expense claims, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListClaims(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var list = await claims.ListAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ExpenseClaimResponse>>.CreateSuccess(
            list.Select(claim => ToResponse(claim, [])).ToArray()));
    }

    /// <summary>Create a draft claim with its expense lines.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateClaim(
        int companyId,
        [FromBody] CreateExpenseClaimWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An expense claim body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var lines = (request.Lines ?? [])
            .Select(line => new ExpenseClaimLineInput(line.ExpenseChartOfAccountId, line.Description ?? string.Empty, line.Amount))
            .ToArray();
        var claim = await claims.CreateAsync(
            new CreateExpenseClaimRequest(companyId, request.ClaimantName ?? string.Empty, request.ClaimDate, request.Description ?? string.Empty, lines),
            actor,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetClaim),
            new { companyId, claimId = claim.Id },
            ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(claim, [])));
    }

    /// <summary>One claim with its expense lines.</summary>
    [HttpGet("{claimId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetClaim(int companyId, int claimId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var view = await claims.GetAsync(companyId, claimId, cancellationToken);
        if (view is null)
            return NotFound(ApiResponse<object>.CreateFailure("Expense claim not found in this company."));

        return Ok(ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(view.Claim, view.Lines)));
    }

    /// <summary>Submit a draft claim for review.</summary>
    [HttpPost("{claimId:int}/submit")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubmitClaim(int companyId, int claimId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var claim = await claims.SubmitAsync(companyId, claimId, actor, cancellationToken);
        return Ok(ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(claim, [])));
    }

    /// <summary>Approve a submitted claim; posts its expenses against the employee payable.</summary>
    [HttpPost("{claimId:int}/approval")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApproveClaim(int companyId, int claimId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var claim = await claims.ApproveAsync(companyId, claimId, actor, cancellationToken);
        return Ok(ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(claim, [])));
    }

    /// <summary>Reject a submitted claim with a mandatory reason; the rejection is final.</summary>
    [HttpPost("{claimId:int}/rejection")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RejectClaim(
        int companyId,
        int claimId,
        [FromBody] RejectExpenseClaimWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A rejection body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var claim = await claims.RejectAsync(companyId, claimId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(claim, [])));
    }

    /// <summary>Pay an approved claim from a bank account; settles the employee payable.</summary>
    [HttpPost("{claimId:int}/payment")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PayClaim(
        int companyId,
        int claimId,
        [FromBody] PayExpenseClaimWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A payment body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var claim = await claims.PayAsync(
            companyId, claimId, request.PaymentChartOfAccountId, request.PaymentDate, actor, cancellationToken);
        return Ok(ApiResponse<ExpenseClaimResponse>.CreateSuccess(ToResponse(claim, [])));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static ExpenseClaimResponse ToResponse(ExpenseClaim claim, IReadOnlyList<ExpenseClaimLine> lines) => new(
        claim.Id,
        claim.ClaimNumber,
        claim.ClaimantName,
        claim.ClaimDate,
        claim.Description,
        claim.Status.ToString(),
        claim.TotalAmount,
        claim.SubmittedBy,
        claim.ApprovedBy,
        claim.RejectedBy,
        claim.RejectionReason,
        claim.PaymentDate,
        claim.PaidBy,
        claim.ApprovalJournalEntryId,
        claim.PaymentJournalEntryId,
        lines.Select(line => new ExpenseClaimLineResponse(
            line.Id, line.LineNumber, line.ExpenseChartOfAccountId, line.Description, line.Amount)).ToArray());
}

/// <summary>Claim creation body; ownership comes from the route and session.</summary>
public sealed class CreateExpenseClaimWriteRequest
{
    public string? ClaimantName { get; init; }
    public DateOnly ClaimDate { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ExpenseClaimLineWriteRequest>? Lines { get; init; }
}

/// <summary>One expense on a claim.</summary>
public sealed class ExpenseClaimLineWriteRequest
{
    public int ExpenseChartOfAccountId { get; init; }
    public string? Description { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>Rejection body with the mandatory reviewer reason.</summary>
public sealed class RejectExpenseClaimWriteRequest
{
    public string? Reason { get; init; }
}

/// <summary>Payment body naming the bank account and the date the money left.</summary>
public sealed class PayExpenseClaimWriteRequest
{
    public int PaymentChartOfAccountId { get; init; }
    public DateOnly PaymentDate { get; init; }
}

/// <summary>Claim state; lines are present on the single-claim read only.</summary>
public sealed record ExpenseClaimResponse(
    int Id,
    string ClaimNumber,
    string ClaimantName,
    DateOnly ClaimDate,
    string Description,
    string Status,
    decimal TotalAmount,
    string? SubmittedBy,
    string? ApprovedBy,
    string? RejectedBy,
    string? RejectionReason,
    DateOnly? PaymentDate,
    string? PaidBy,
    int? ApprovalJournalEntryId,
    int? PaymentJournalEntryId,
    IReadOnlyList<ExpenseClaimLineResponse> Lines);

/// <summary>One expense line on a claim.</summary>
public sealed record ExpenseClaimLineResponse(
    int Id,
    int LineNumber,
    int ExpenseChartOfAccountId,
    string Description,
    decimal Amount);
