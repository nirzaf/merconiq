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
/// Bank statements for company bank accounts, reviewed matching of statement lines to ledger lines, and
/// the reconciliation report. Importing needs Edit; matching and withdrawing matches need Approve.
/// Nothing here posts to the ledger.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/bank-statements")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class BankReconciliationController(
    IBankReconciliationService reconciliation,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List imported bank statements for one authorized company, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListStatements(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var statements = await reconciliation.ListStatementsAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<BankStatementSummaryResponse>>.CreateSuccess(
            statements.Select(ToSummary).ToArray()));
    }

    /// <summary>Import one bank statement; it must foot exactly from its opening balance to its closing balance.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ImportStatement(
        int companyId,
        [FromBody] ImportBankStatementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A bank statement body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var lines = (request.Lines ?? [])
            .Select(line => new BankStatementLineInput(line.ValueDate, line.Description ?? string.Empty, line.Reference, line.Amount))
            .ToArray();
        var statement = await reconciliation.ImportStatementAsync(
            new BankStatementImportRequest(
                companyId,
                request.ChartOfAccountId,
                request.StatementDate,
                request.OpeningBalance,
                request.ClosingBalance,
                request.Reference,
                lines),
            actor,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetStatement),
            new { companyId, bankStatementId = statement.Id },
            ApiResponse<BankStatementSummaryResponse>.CreateSuccess(ToSummary(statement)));
    }

    /// <summary>One bank statement with its lines and their active ledger matches.</summary>
    [HttpGet("{bankStatementId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetStatement(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var view = await reconciliation.GetStatementAsync(companyId, bankStatementId, cancellationToken);
        if (view is null)
            return NotFound(ApiResponse<object>.CreateFailure("Bank statement not found in this company."));

        var statement = view.Statement;
        return Ok(ApiResponse<BankStatementResponse>.CreateSuccess(new BankStatementResponse(
            statement.Id,
            statement.ChartOfAccountId,
            statement.StatementDate,
            statement.OpeningBalance,
            statement.ClosingBalance,
            statement.Reference,
            statement.ImportedBy,
            statement.ImportedAtUtc,
            statement.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new BankStatementLineResponse(
                    line.Id,
                    line.LineNumber,
                    line.ValueDate,
                    line.Description,
                    line.Reference,
                    line.Amount,
                    view.ActiveMatchesByStatementLineId.TryGetValue(line.Id, out var journalLineId)
                        ? journalLineId
                        : null))
                .ToArray())));
    }

    /// <summary>Reconcile one statement against the bank account's ledger as of the statement date.</summary>
    [HttpGet("{bankStatementId:int}/reconciliation")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetReconciliation(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var report = await reconciliation.GetReconciliationAsync(companyId, bankStatementId, cancellationToken);
        return Ok(ApiResponse<BankReconciliationResponse>.CreateSuccess(new BankReconciliationResponse(
            report.CompanyId,
            report.BankStatementId,
            report.ChartOfAccountId,
            report.StatementDate,
            report.ClosingBalance,
            report.LedgerBalance,
            report.UnmatchedLedgerAmount,
            report.UnmatchedStatementAmount,
            report.AdjustedBankBalance,
            report.AdjustedBookBalance,
            report.Difference,
            report.MatchedStatementLineCount,
            report.UnmatchedStatementLineCount,
            report.UnmatchedLedgerLineCount,
            report.IsReconciled,
            report.UnmatchedLedgerItems.Select(item => new UnmatchedLedgerItemResponse(
                item.JournalLineId,
                item.JournalEntryId,
                item.PostingDate,
                item.Amount,
                item.Description)).ToArray())));
    }

    /// <summary>Confirm that one statement line and one ledger line on the same account are the same movement.</summary>
    [HttpPost("{bankStatementId:int}/lines/{bankStatementLineId:int}/match")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MatchLine(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        [FromBody] MatchBankStatementLineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A match request body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var match = await reconciliation.MatchLineAsync(
            companyId, bankStatementId, bankStatementLineId, request.JournalLineId, actor, cancellationToken);
        return Ok(ApiResponse<BankStatementMatchResponse>.CreateSuccess(ToMatchResponse(match)));
    }

    /// <summary>Withdraw an active match with a mandatory reason; the withdrawn match stays on record.</summary>
    [HttpPost("{bankStatementId:int}/lines/{bankStatementLineId:int}/unmatch")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UnmatchLine(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        [FromBody] UnmatchBankStatementLineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An unmatch request body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var match = await reconciliation.UnmatchLineAsync(
            companyId, bankStatementId, bankStatementLineId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<BankStatementMatchResponse>.CreateSuccess(ToMatchResponse(match)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static BankStatementSummaryResponse ToSummary(BankStatement statement) => new(
        statement.Id,
        statement.ChartOfAccountId,
        statement.StatementDate,
        statement.OpeningBalance,
        statement.ClosingBalance,
        statement.Reference,
        statement.ImportedBy,
        statement.ImportedAtUtc);

    private static BankStatementMatchResponse ToMatchResponse(BankStatementMatch match) => new(
        match.Id,
        match.BankStatementLineId,
        match.JournalLineId,
        match.IsActive,
        match.MatchedBy,
        match.MatchedAtUtc,
        match.UnmatchedBy,
        match.UnmatchedAtUtc,
        match.UnmatchReason);
}

/// <summary>Bank statement import body; ownership comes from the route and session.</summary>
public sealed class ImportBankStatementRequest
{
    public int ChartOfAccountId { get; init; }
    public DateOnly StatementDate { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal ClosingBalance { get; init; }
    public string? Reference { get; init; }
    public IReadOnlyList<ImportBankStatementLineRequest>? Lines { get; init; }
}

/// <summary>One bank movement; a positive amount is money into the bank account.</summary>
public sealed class ImportBankStatementLineRequest
{
    public DateOnly ValueDate { get; init; }
    public string? Description { get; init; }
    public string? Reference { get; init; }
    public decimal Amount { get; init; }
}

/// <summary>Names the ledger line to confirm against a statement line.</summary>
public sealed class MatchBankStatementLineRequest
{
    public int JournalLineId { get; init; }
}

/// <summary>Withdrawal request carrying the mandatory reviewer reason.</summary>
public sealed class UnmatchBankStatementLineRequest
{
    public string? Reason { get; init; }
}

/// <summary>Statement header without lines, for listings.</summary>
public sealed record BankStatementSummaryResponse(
    int Id,
    int ChartOfAccountId,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string? Reference,
    string ImportedBy,
    DateTime ImportedAtUtc);

/// <summary>One statement with its lines.</summary>
public sealed record BankStatementResponse(
    int Id,
    int ChartOfAccountId,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string? Reference,
    string ImportedBy,
    DateTime ImportedAtUtc,
    IReadOnlyList<BankStatementLineResponse> Lines);

/// <summary>One statement line and the ledger line it is currently matched to, if any.</summary>
public sealed record BankStatementLineResponse(
    int Id,
    int LineNumber,
    DateOnly ValueDate,
    string Description,
    string? Reference,
    decimal Amount,
    int? MatchedJournalLineId);

/// <summary>A ledger line on the bank account that no active statement match explains yet.</summary>
public sealed record UnmatchedLedgerItemResponse(
    int JournalLineId,
    int JournalEntryId,
    DateOnly PostingDate,
    decimal Amount,
    string Description);

/// <summary>Reconciliation figures as of the statement date; see the report model for the formulas.</summary>
public sealed record BankReconciliationResponse(
    int CompanyId,
    int BankStatementId,
    int ChartOfAccountId,
    DateOnly StatementDate,
    decimal ClosingBalance,
    decimal LedgerBalance,
    decimal UnmatchedLedgerAmount,
    decimal UnmatchedStatementAmount,
    decimal AdjustedBankBalance,
    decimal AdjustedBookBalance,
    decimal Difference,
    int MatchedStatementLineCount,
    int UnmatchedStatementLineCount,
    int UnmatchedLedgerLineCount,
    bool IsReconciled,
    IReadOnlyList<UnmatchedLedgerItemResponse> UnmatchedLedgerItems);

/// <summary>A statement-line match record, active or withdrawn.</summary>
public sealed record BankStatementMatchResponse(
    int Id,
    int BankStatementLineId,
    int JournalLineId,
    bool IsActive,
    string MatchedBy,
    DateTime MatchedAtUtc,
    string? UnmatchedBy,
    DateTime? UnmatchedAtUtc,
    string? UnmatchReason);
