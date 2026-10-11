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
/// Bounded returns and credit notes against posted receipts and deliveries, settled through the
/// finance kernel. Posting requires the company Post capability; notes are immutable and
/// idempotent per request key.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/credit-notes")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class CreditNotesController(
    ICreditNoteService creditNotes,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Post one supplier credit note returning received goods.</summary>
    [HttpPost("purchase")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostPurchase(
        int companyId,
        [FromBody] PurchaseCreditNoteWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        var requestKey = RequireIdempotencyKey();
        if (requestKey is null)
            return BadRequest(ApiResponse<object>.CreateFailure(
                "Idempotency-Key is required for credit-note posting."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var note = await creditNotes.PostPurchaseCreditNoteAsync(
            new PostPurchaseCreditNoteRequest(
                companyId,
                request.SupplierId,
                request.CreditNoteNumber,
                request.Reason,
                requestKey,
                request.Lines.Select(line => new PostPurchaseCreditNoteLine(
                    line.GoodsReceiptLineId, line.Quantity)).ToArray(),
                request.PostingDate,
                request.Notes),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<CreditNotePostResponse>.CreateSuccess(new CreditNotePostResponse(
            note.Id, note.CreditNoteNumber, note.TotalValue, Replayed: false)));
    }

    /// <summary>Post one sales credit note returning delivered goods.</summary>
    [HttpPost("sales")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostSales(
        int companyId,
        [FromBody] SalesCreditNoteWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        var requestKey = RequireIdempotencyKey();
        if (requestKey is null)
            return BadRequest(ApiResponse<object>.CreateFailure(
                "Idempotency-Key is required for credit-note posting."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var note = await creditNotes.PostSalesCreditNoteAsync(
            new PostSalesCreditNoteRequest(
                companyId,
                request.CustomerId,
                request.CreditNoteNumber,
                request.Reason,
                requestKey,
                request.Lines.Select(line => new PostSalesCreditNoteLine(
                    line.DeliveryNoteLineId, line.Quantity, line.Disposition)).ToArray(),
                request.PostingDate,
                request.Notes),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<CreditNotePostResponse>.CreateSuccess(new CreditNotePostResponse(
            note.Id, note.CreditNoteNumber, note.TotalValue, Replayed: false)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private string? RequireIdempotencyKey()
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}

public sealed class PurchaseCreditNoteWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    [Required, StringLength(128)]
    public string CreditNoteNumber { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PurchaseCreditNoteLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? PostingDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class PurchaseCreditNoteLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int GoodsReceiptLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed class SalesCreditNoteWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int CustomerId { get; init; }

    [Required, StringLength(128)]
    public string CreditNoteNumber { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<SalesCreditNoteLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? PostingDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class SalesCreditNoteLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int DeliveryNoteLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    public StockReturnDisposition Disposition { get; init; } = StockReturnDisposition.Restockable;
}

public sealed record CreditNotePostResponse(
    int CreditNoteId, string CreditNoteNumber, decimal TotalValue, bool Replayed);
