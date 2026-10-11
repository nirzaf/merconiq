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
/// Posted supplier invoices matched to posted goods receipts. Posting requires the company Post
/// capability; invoices are immutable, clear GRNI exactly, and recognize the payable once.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/purchase-invoices")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class PurchaseInvoicesController(
    IPurchaseInvoiceService purchaseInvoices,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    private const int MaximumPageSize = 100;

    /// <summary>List posted supplier invoices, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetAll(
        int companyId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
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

        var invoices = await purchaseInvoices.ListPurchaseInvoicesAsync(
            companyId, (int)offset, pageSize + 1, cancellationToken);
        var hasMore = invoices.Count > pageSize;
        return Ok(ApiResponse<PurchaseInvoicePageResponse>.CreateSuccess(new PurchaseInvoicePageResponse(
            page, pageSize, hasMore, invoices.Take(pageSize).Select(ToResponse).ToArray())));
    }

    /// <summary>Get one posted invoice with its matched lines.</summary>
    [HttpGet("{purchaseInvoiceId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetById(
        int companyId,
        int purchaseInvoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var invoice = await purchaseInvoices.GetPurchaseInvoiceAsync(companyId, purchaseInvoiceId, cancellationToken);
        if (invoice is null)
            return NotFound(ApiResponse<object>.CreateFailure("Purchase invoice not found in this company."));
        return Ok(ApiResponse<PurchaseInvoiceDetailResponse>.CreateSuccess(new PurchaseInvoiceDetailResponse(
            ToResponse(invoice),
            invoice.Lines.OrderBy(line => line.LineNumber).Select(line => new PurchaseInvoiceLineResponse(
                line.Id, line.LineNumber, line.GoodsReceiptLineId, line.ItemId, line.MatchedValue)).ToArray())));
    }

    /// <summary>Open payable items derived from posted invoices minus posted allocations.</summary>
    [HttpGet("payables-open")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetPayablesOpen(
        int companyId,
        [FromQuery] int? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var summary = await purchaseInvoices.GetPayablesOpenAsync(companyId, supplierId, cancellationToken);
        return Ok(ApiResponse<PayablesOpenResponse>.CreateSuccess(new PayablesOpenResponse(
            summary.CompanyId,
            summary.Items.Select(item => new PayableOpenItemResponse(
                item.PurchaseInvoiceId, item.SupplierId, item.InvoiceNumber,
                item.InvoiceDate, item.DueDate, item.TotalValue, item.PaidValue, item.OpenValue)).ToArray(),
            summary.TotalOpenValue)));
    }

    /// <summary>
    /// Post one supplier invoice matched to posted receipt lines. Requires an Idempotency-Key
    /// header; committed requests replay, conflicting payloads are rejected. The matched GRNI
    /// value clears and the payable recognizes in one atomic journal.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(
        int companyId,
        [FromBody] PostPurchaseInvoiceWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;
        if (actor is null)
            return Unauthorized();

        var requestKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(requestKey))
            return BadRequest(ApiResponse<object>.CreateFailure(
                "Idempotency-Key is required for purchase-invoice posting."));

        var result = await purchaseInvoices.PostPurchaseInvoiceAsync(
            new PostPurchaseInvoiceRequest(
                companyId,
                request.SupplierId,
                request.InvoiceNumber,
                request.InvoiceDate,
                request.DueDate,
                requestKey,
                request.Lines.Select(line => new PostPurchaseInvoiceLine(line.GoodsReceiptLineId)).ToArray(),
                request.PostingDate,
                request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<PurchaseInvoicePostResponse>.CreateSuccess(new PurchaseInvoicePostResponse(
            result.PurchaseInvoiceId,
            result.InvoiceNumber,
            result.PostingDate,
            result.TotalValue,
            result.OpenValue,
            result.JournalNumber,
            result.Replayed)));
    }

    private static PurchaseInvoiceResponse ToResponse(PurchaseInvoice invoice) => new(
        invoice.Id,
        invoice.CompanyId,
        invoice.SupplierId,
        invoice.InvoiceNumber,
        invoice.InvoiceDate,
        invoice.DueDate,
        invoice.PostingDate,
        invoice.TotalValue,
        invoice.Notes,
        invoice.PostedAtUtc,
        invoice.PostedBy);
}

/// <summary>Supplier-invoice posting request; every line matches one posted receipt line.</summary>
public sealed class PostPurchaseInvoiceWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    [Required, StringLength(128)]
    public string InvoiceNumber { get; init; } = string.Empty;

    public DateOnly InvoiceDate { get; init; }

    public DateOnly DueDate { get; init; }

    public DateOnly? PostingDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PurchaseInvoiceLineWriteRequest> Lines { get; init; } = [];
}

/// <summary>One matched invoice line.</summary>
public sealed class PurchaseInvoiceLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int GoodsReceiptLineId { get; init; }
}

/// <summary>A bounded page of posted purchase invoices.</summary>
public sealed record PurchaseInvoicePageResponse(
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<PurchaseInvoiceResponse> Items);

/// <summary>Purchase-invoice header fields exposed by the API.</summary>
public sealed record PurchaseInvoiceResponse(
    int Id,
    int CompanyId,
    int SupplierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    DateOnly PostingDate,
    decimal TotalValue,
    string? Notes,
    DateTime PostedAtUtc,
    string PostedBy);

/// <summary>One invoice with its matched lines.</summary>
public sealed record PurchaseInvoiceDetailResponse(
    PurchaseInvoiceResponse Invoice,
    IReadOnlyList<PurchaseInvoiceLineResponse> Lines);

/// <summary>One matched invoice line.</summary>
public sealed record PurchaseInvoiceLineResponse(
    int Id,
    int LineNumber,
    int GoodsReceiptLineId,
    int ItemId,
    decimal MatchedValue);

/// <summary>The committed-or-replayed invoice posting outcome.</summary>
public sealed record PurchaseInvoicePostResponse(
    int PurchaseInvoiceId,
    string InvoiceNumber,
    DateOnly PostingDate,
    decimal TotalValue,
    decimal OpenValue,
    string? JournalNumber,
    bool Replayed);

/// <summary>Open payables derived from posted evidence.</summary>
public sealed record PayablesOpenResponse(
    int CompanyId,
    IReadOnlyList<PayableOpenItemResponse> Items,
    decimal TotalOpenValue);

/// <summary>One open payable item.</summary>
public sealed record PayableOpenItemResponse(
    int PurchaseInvoiceId,
    int SupplierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal TotalValue,
    decimal PaidValue,
    decimal OpenValue);
