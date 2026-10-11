using System.ComponentModel.DataAnnotations;
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
/// Company-scoped general-ledger journals. Posting requires the company Post capability;
/// reversals require the separate Reverse capability. Journals are immutable once committed.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/journals")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class JournalsController(
    IJournalPostingService postingService,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    private const int MaximumPageSize = 100;

    /// <summary>List posted journals for one authorized company, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetAll(
        int companyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
        {
            return BadRequest(ApiResponse<object>.CreateFailure(
                $"Page must be at least 1 and pageSize must be between 1 and {MaximumPageSize}."));
        }

        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
            return BadRequest(ApiResponse<object>.CreateFailure("The requested page is too large."));

        var items = await postingService.ListJournalsAsync(
            companyId, fromDate, toDate, (int)offset, pageSize + 1, cancellationToken);
        var hasMore = items.Count > pageSize;
        return Ok(ApiResponse<JournalPageResponse>.CreateSuccess(new JournalPageResponse(
            page,
            pageSize,
            hasMore,
            items.Take(pageSize).Select(ToHeaderResponse).ToArray())));
    }

    /// <summary>Get one posted journal with its immutable lines.</summary>
    [HttpGet("{journalId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetById(
        int companyId,
        int journalId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var journal = await postingService.GetJournalAsync(companyId, journalId, cancellationToken);
        if (journal is null)
            return NotFound(ApiResponse<object>.CreateFailure("Journal not found in this company."));
        return Ok(ApiResponse<JournalDetailResponse>.CreateSuccess(ToDetailResponse(journal)));
    }

    /// <summary>
    /// Commit one balanced journal for a named source action. Committed actions replay their
    /// result; conflicting payloads are rejected with a conflict response.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(
        int companyId,
        [FromBody] JournalPostRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var result = await postingService.PostAsync(
            new JournalPostingRequest(
                companyId,
                request.PostingDate,
                request.CurrencyCode,
                request.Description,
                request.SourceDocumentType,
                request.SourceDocumentId,
                request.SourceAction,
                request.SourceVersion,
                request.Lines
                    .Select(line => new JournalLineInput(line.ChartOfAccountId, line.Description, line.Debit, line.Credit))
                    .ToArray()),
            actor,
            cancellationToken);
        return Ok(ApiResponse<JournalPostingResponse>.CreateSuccess(ToPostingResponse(result)));
    }

    /// <summary>Post one source-linked reversal of a posted journal.</summary>
    [HttpPost("{journalId:int}/reversals")]
    [Authorize(Policy = CapabilityPolicies.Reverse)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Reverse(
        int companyId,
        int journalId,
        [FromBody] JournalReversalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Reverse))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var result = await postingService.ReverseAsync(
            new ReverseJournalRequest(companyId, journalId, request?.Reason ?? string.Empty, request?.PostingDate),
            actor,
            cancellationToken);
        return Ok(ApiResponse<JournalPostingResponse>.CreateSuccess(ToPostingResponse(result)));
    }

    /// <summary>Ledger-backed trial balance for a date window; debits always equal credits.</summary>
    [HttpGet("trial-balance")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetTrialBalance(
        int companyId,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var trialBalance = await postingService.GetTrialBalanceAsync(companyId, fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<TrialBalanceResponse>.CreateSuccess(new TrialBalanceResponse(
            trialBalance.CompanyId,
            trialBalance.FromDate,
            trialBalance.ToDate,
            trialBalance.Rows.Select(row => new TrialBalanceRowResponse(
                row.ChartOfAccountId, row.AccountCode, row.AccountName, row.Debit, row.Credit)).ToArray(),
            trialBalance.TotalDebit,
            trialBalance.TotalCredit)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static JournalPostingResponse ToPostingResponse(JournalPostingResult result) => new(
        result.JournalEntryId,
        result.JournalNumber,
        result.PostingDate,
        result.TotalDebit,
        result.TotalCredit,
        result.Replayed);

    private static JournalHeaderResponse ToHeaderResponse(JournalEntry entry) => new(
        entry.Id,
        entry.JournalNumber,
        entry.PostingDate,
        entry.CurrencyCode,
        entry.Description,
        entry.SourceDocumentType,
        entry.SourceDocumentId,
        entry.SourceAction,
        entry.SourceVersion,
        entry.TotalDebit,
        entry.TotalCredit,
        entry.ReversalOfJournalEntryId,
        entry.ReversalReason,
        entry.PostedAtUtc,
        entry.PostedBy);

    private static JournalDetailResponse ToDetailResponse(JournalEntry entry) => new(
        ToHeaderResponse(entry),
        entry.Lines.OrderBy(line => line.LineNumber).Select(line => new JournalLineResponse(
            line.Id,
            line.LineNumber,
            line.ChartOfAccountId,
            line.AccountCodeSnapshot,
            line.AccountNameSnapshot,
            line.Description,
            line.Debit,
            line.Credit)).ToArray());
}

/// <summary>Journal posting request; line amounts must already be at the company currency scale.</summary>
public sealed class JournalPostRequest
{
    public int CompanyId { get; init; }
    public DateOnly PostingDate { get; init; }

    [Required, StringLength(3)]
    public string CurrencyCode { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [Required, StringLength(64)]
    public string SourceDocumentType { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string SourceDocumentId { get; init; } = string.Empty;

    [Required, StringLength(64)]
    public string SourceAction { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int SourceVersion { get; init; } = 1;

    [MinLength(2), MaxLength(500)]
    public IReadOnlyList<JournalLineWriteRequest> Lines { get; init; } = [];
}

/// <summary>One requested journal line; exactly one of debit or credit must be positive.</summary>
public sealed class JournalLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ChartOfAccountId { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Debit { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Credit { get; init; }
}

/// <summary>Reversal request carrying the mandatory reviewer reason.</summary>
public sealed class JournalReversalRequest
{
    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public DateOnly? PostingDate { get; init; }
}

/// <summary>The committed-or-replayed posting outcome.</summary>
public sealed record JournalPostingResponse(
    int JournalEntryId,
    string JournalNumber,
    DateOnly PostingDate,
    decimal TotalDebit,
    decimal TotalCredit,
    bool Replayed);

/// <summary>A bounded page of posted journals.</summary>
public sealed record JournalPageResponse(
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<JournalHeaderResponse> Items);

/// <summary>Journal header fields exposed by the API.</summary>
public sealed record JournalHeaderResponse(
    int Id,
    string JournalNumber,
    DateOnly PostingDate,
    string CurrencyCode,
    string Description,
    string SourceDocumentType,
    string SourceDocumentId,
    string SourceAction,
    int SourceVersion,
    decimal TotalDebit,
    decimal TotalCredit,
    int? ReversalOfJournalEntryId,
    string? ReversalReason,
    DateTime PostedAtUtc,
    string PostedBy);

/// <summary>One journal with its immutable lines.</summary>
public sealed record JournalDetailResponse(
    JournalHeaderResponse Journal,
    IReadOnlyList<JournalLineResponse> Lines);

/// <summary>One immutable journal line.</summary>
public sealed record JournalLineResponse(
    int Id,
    int LineNumber,
    int ChartOfAccountId,
    string AccountCodeSnapshot,
    string AccountNameSnapshot,
    string? Description,
    decimal Debit,
    decimal Credit);

/// <summary>Ledger-backed trial balance response.</summary>
public sealed record TrialBalanceResponse(
    int CompanyId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<TrialBalanceRowResponse> Rows,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>One account's movement inside the trial-balance window.</summary>
public sealed record TrialBalanceRowResponse(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit);
