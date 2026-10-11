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
/// Posted goods receipts against approved purchase orders. Posting requires the company Post
/// capability; receipts are immutable and one request key commits exactly once, including the
/// valued stock intake and the Inventory/GRNI journal.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/goods-receipts")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class GoodsReceiptsController(
    IGoodsReceiptService goodsReceipts,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    private const int MaximumPageSize = 100;

    /// <summary>List posted goods receipts for one authorized company, newest first.</summary>
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

        var receipts = await goodsReceipts.ListGoodsReceiptsAsync(
            companyId, (int)offset, pageSize + 1, cancellationToken);
        var hasMore = receipts.Count > pageSize;
        return Ok(ApiResponse<GoodsReceiptPageResponse>.CreateSuccess(new GoodsReceiptPageResponse(
            page,
            pageSize,
            hasMore,
            receipts.Take(pageSize).Select(ToResponse).ToArray())));
    }

    /// <summary>Get one posted receipt with its immutable lines.</summary>
    [HttpGet("{goodsReceiptId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetById(
        int companyId,
        int goodsReceiptId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var receipt = await goodsReceipts.GetGoodsReceiptAsync(companyId, goodsReceiptId, cancellationToken);
        if (receipt is null)
            return NotFound(ApiResponse<object>.CreateFailure("Goods receipt not found in this company."));
        return Ok(ApiResponse<GoodsReceiptDetailResponse>.CreateSuccess(new GoodsReceiptDetailResponse(
            ToResponse(receipt),
            receipt.Lines.OrderBy(line => line.LineNumber).Select(ToLineResponse).ToArray())));
    }

    /// <summary>
    /// Post one goods receipt against an approved purchase order. Requires an Idempotency-Key
    /// header; committed requests replay, conflicting payloads are rejected. Accepted quantities
    /// enter valued stock and one Inventory/GRNI journal commits atomically.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(
        int companyId,
        [FromBody] PostGoodsReceiptWriteRequest request,
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
                "Idempotency-Key is required for goods-receipt posting."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var result = await goodsReceipts.PostGoodsReceiptAsync(
            new PostGoodsReceiptRequest(
                companyId,
                request.PurchaseOrderId,
                request.LocationId,
                requestKey,
                request.Lines
                    .Select(line => new PostGoodsReceiptLine(
                        line.PurchaseOrderLineId,
                        line.AcceptedQuantity,
                        line.RejectedQuantity,
                        line.BatchNumber,
                        line.ExpiryDate))
                    .ToArray(),
                request.PostingDate,
                request.SupplierDeliveryReference,
                request.Notes),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<GoodsReceiptPostResponse>.CreateSuccess(new GoodsReceiptPostResponse(
            result.GoodsReceiptId,
            result.ReceiptNumber,
            result.PostingDate,
            result.AcceptedQuantity,
            result.AcceptedValue,
            result.JournalNumber,
            result.Replayed)));
    }

    private static GoodsReceiptResponse ToResponse(GoodsReceipt receipt) => new(
        receipt.Id,
        receipt.CompanyId,
        receipt.PurchaseOrderId,
        receipt.LocationId,
        receipt.ReceiptNumber,
        receipt.PostingDate,
        receipt.SupplierDeliveryReference,
        receipt.Notes,
        receipt.ReceivedAtUtc,
        receipt.ReceivedBy);

    private static GoodsReceiptLineResponse ToLineResponse(GoodsReceiptLine line) => new(
        line.Id,
        line.LineNumber,
        line.PurchaseOrderLineId,
        line.ItemId,
        line.AcceptedQuantity,
        line.RejectedQuantity,
        line.BaseQuantity,
        line.UnitCost,
        line.BatchNumber,
        line.ExpiryDate);
}

/// <summary>Goods-receipt posting request; quantities are in purchase-order line units.</summary>
public sealed class PostGoodsReceiptWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int PurchaseOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int LocationId { get; init; }

    public DateOnly? PostingDate { get; init; }

    [StringLength(128)]
    public string? SupplierDeliveryReference { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<GoodsReceiptLineWriteRequest> Lines { get; init; } = [];
}

/// <summary>One requested receipt line; at least one of accepted or rejected must be positive.</summary>
public sealed class GoodsReceiptLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int PurchaseOrderLineId { get; init; }

    [Range(0, int.MaxValue)]
    public int AcceptedQuantity { get; init; }

    [Range(0, int.MaxValue)]
    public int RejectedQuantity { get; init; }

    [StringLength(100)]
    public string? BatchNumber { get; init; }

    public DateTime? ExpiryDate { get; init; }
}

/// <summary>A bounded page of posted goods receipts.</summary>
public sealed record GoodsReceiptPageResponse(
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<GoodsReceiptResponse> Items);

/// <summary>Goods-receipt header fields exposed by the API.</summary>
public sealed record GoodsReceiptResponse(
    int Id,
    int CompanyId,
    int PurchaseOrderId,
    int LocationId,
    string ReceiptNumber,
    DateOnly PostingDate,
    string? SupplierDeliveryReference,
    string? Notes,
    DateTime ReceivedAtUtc,
    string ReceivedBy);

/// <summary>One receipt with its immutable lines.</summary>
public sealed record GoodsReceiptDetailResponse(
    GoodsReceiptResponse Receipt,
    IReadOnlyList<GoodsReceiptLineResponse> Lines);

/// <summary>One immutable goods-receipt line.</summary>
public sealed record GoodsReceiptLineResponse(
    int Id,
    int LineNumber,
    int PurchaseOrderLineId,
    int ItemId,
    int AcceptedQuantity,
    int RejectedQuantity,
    int BaseQuantity,
    decimal UnitCost,
    string? BatchNumber,
    DateTime? ExpiryDate);

/// <summary>The committed-or-replayed receipt posting outcome.</summary>
public sealed record GoodsReceiptPostResponse(
    int GoodsReceiptId,
    string ReceiptNumber,
    DateOnly PostingDate,
    int AcceptedQuantity,
    decimal AcceptedValue,
    string? JournalNumber,
    bool Replayed);
