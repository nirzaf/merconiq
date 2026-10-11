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
/// Order-to-cash posting: sales orders (commitments only), stock-issuing deliveries with COGS,
/// and revenue/AR sales invoices. Each operation is idempotent per request key.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/sales")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class SalesController(
    ISalesService sales,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create one approved sales order. Commitment only; nothing posts.</summary>
    [HttpPost("orders")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostOrder(
        int companyId,
        [FromBody] PostSalesOrderWriteRequest request,
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
                "Idempotency-Key is required for sales-order creation."));

        var result = await sales.PostSalesOrderAsync(
            new PostSalesOrderRequest(
                companyId,
                request.CustomerId,
                requestKey,
                request.Lines.Select(line => new PostSalesOrderLine(line.ItemId, line.Quantity, line.UnitPrice)).ToArray(),
                request.OrderDate,
                request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<SalesOrderPostResponse>.CreateSuccess(new SalesOrderPostResponse(
            result.SalesOrderId, result.FirstLineId, result.OrderNumber, result.TotalValue, result.Replayed)));
    }

    /// <summary>Post one delivery against an approved sales order; issues valued stock and books COGS.</summary>
    [HttpPost("deliveries")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostDelivery(
        int companyId,
        [FromBody] PostDeliveryWriteRequest request,
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
                "Idempotency-Key is required for delivery posting."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var result = await sales.PostDeliveryAsync(
            new PostDeliveryRequest(
                companyId,
                request.SalesOrderId,
                request.LocationId,
                requestKey,
                request.Lines.Select(line => new PostDeliveryLine(
                    line.SalesOrderLineId, line.Quantity, line.BatchNumber, line.ExpiryDate)).ToArray(),
                request.PostingDate,
                request.Notes),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<DeliveryPostResponse>.CreateSuccess(new DeliveryPostResponse(
            result.DeliveryNoteId,
            result.FirstLineId,
            result.DeliveryNumber,
            result.PostingDate,
            result.DeliveredQuantity,
            result.CostOfGoodsSold,
            result.JournalNumber,
            result.Replayed)));
    }

    /// <summary>Post one sales invoice matched to delivered lines; recognizes revenue and AR once.</summary>
    [HttpPost("invoices")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostInvoice(
        int companyId,
        [FromBody] PostSalesInvoiceWriteRequest request,
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
                "Idempotency-Key is required for sales-invoice posting."));

        var result = await sales.PostSalesInvoiceAsync(
            new PostSalesInvoiceRequest(
                companyId,
                request.CustomerId,
                request.InvoiceNumber,
                request.InvoiceDate,
                request.DueDate,
                requestKey,
                request.Lines.Select(line => new PostSalesInvoiceLine(line.DeliveryNoteLineId)).ToArray(),
                request.PostingDate,
                request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<SalesInvoicePostResponse>.CreateSuccess(new SalesInvoicePostResponse(
            result.SalesInvoiceId,
            result.InvoiceNumber,
            result.PostingDate,
            result.TotalValue,
            result.OpenValue,
            result.JournalNumber,
            result.Replayed)));
    }

    /// <summary>Delivery progress for one order, derived from posted delivery lines.</summary>
    [HttpGet("orders/{salesOrderId:int}/progress")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetProgress(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var progress = await sales.GetSalesOrderProgressAsync(companyId, salesOrderId, cancellationToken);
        return progress is null
            ? NotFound(ApiResponse<object>.CreateFailure("Sales order not found in this company."))
            : Ok(ApiResponse<SalesOrderProgressResponse>.CreateSuccess(new SalesOrderProgressResponse(
                progress.SalesOrderId,
                progress.OrderedQuantity,
                progress.DeliveredQuantity,
                progress.OutstandingQuantity,
                progress.Lines.Select(line => new SalesOrderLineProgressResponse(
                    line.LineId, line.ItemId, line.OrderedQuantity, line.DeliveredQuantity, line.OutstandingQuantity))
                    .ToArray())));
    }

    /// <summary>Create one quotation; idempotent per Idempotency-Key. Quotations never post.</summary>
    [HttpPost("quotations")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostQuotation(
        int companyId,
        [FromBody] PostQuotationWriteRequest request,
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
                "Idempotency-Key is required for quotation creation."));

        var result = await sales.PostQuotationAsync(
            new PostQuotationRequest(
                companyId,
                request.CustomerId,
                requestKey,
                request.Lines.Select(line => new PostQuotationLine(line.ItemId, line.Quantity, line.UnitPrice)).ToArray(),
                request.QuotationDate,
                request.ValidUntil,
                request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<QuotationPostResponse>.CreateSuccess(new QuotationPostResponse(
            result.QuotationId, result.QuotationNumber, result.TotalValue, result.ValidUntil, result.Replayed)));
    }

    /// <summary>
    /// Convert one open, unexpired quotation into a sales order; repeated calls replay the
    /// original order.
    /// </summary>
    [HttpPost("quotations/{quotationId:int}/conversions")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConvertQuotation(
        int companyId,
        int quotationId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var result = await sales.ConvertQuotationAsync(
            new ConvertQuotationRequest(companyId, quotationId), actor, cancellationToken);
        return Ok(ApiResponse<QuotationConversionResponse>.CreateSuccess(new QuotationConversionResponse(
            result.QuotationId, result.SalesOrderId, result.OrderNumber, result.TotalValue, result.Replayed)));
    }

    /// <summary>Get one quotation with its lines.</summary>
    [HttpGet("quotations/{quotationId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetQuotation(
        int companyId,
        int quotationId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var quotation = await sales.GetQuotationAsync(companyId, quotationId, cancellationToken);
        return quotation is null
            ? NotFound(ApiResponse<object>.CreateFailure("Quotation not found in this company."))
            : Ok(ApiResponse<QuotationResponse>.CreateSuccess(new QuotationResponse(
                quotation.Id,
                quotation.CompanyId,
                quotation.CustomerId,
                quotation.QuotationNumber,
                quotation.Status.ToString(),
                quotation.QuotationDate,
                quotation.ValidUntil,
                quotation.SalesOrderId,
                quotation.Lines.OrderBy(line => line.LineNumber).Select(line => new QuotationLineResponse(
                    line.Id, line.LineNumber, line.ItemId, line.Quantity, line.UnitPrice, line.NetValue)).ToArray())));
    }

    /// <summary>Customer aging as of a date: open receivable balances bucketed by days past due.</summary>
    [HttpGet("receivables-aging")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetReceivablesAging(
        int companyId,
        [FromQuery] DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var report = await sales.GetReceivablesAgingAsync(companyId, asOfDate, cancellationToken);
        return Ok(ApiResponse<AgingResponse>.CreateSuccess(new AgingResponse(
            report.CompanyId,
            report.AsOfDate,
            report.Kind,
            report.Rows.Select(row => new AgingRowResponse(
                row.PartyId, row.PartyName, row.NotDue, row.Days1To30, row.Days31To60,
                row.Days61To90, row.Over90, row.TotalOpen)).ToArray(),
            report.TotalOpen)));
    }

    /// <summary>Get one sales order with its lines.</summary>
    [HttpGet("orders/{salesOrderId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetOrder(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var order = await sales.GetSalesOrderAsync(companyId, salesOrderId, cancellationToken);
        return order is null
            ? NotFound(ApiResponse<object>.CreateFailure("Sales order not found in this company."))
            : Ok(ApiResponse<SalesOrderResponse>.CreateSuccess(new SalesOrderResponse(
                order.Id,
                order.CompanyId,
                order.CustomerId,
                order.OrderNumber,
                order.OrderDate,
                order.Notes,
                order.Lines.OrderBy(line => line.LineNumber).Select(line => new SalesOrderLineResponse(
                    line.Id, line.LineNumber, line.ItemId, line.Quantity, line.UnitPrice, line.NetValue)).ToArray())));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private string? RequireIdempotencyKey()
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }
}

public sealed class PostSalesOrderWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int CustomerId { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<SalesOrderLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? OrderDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class SalesOrderLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; init; }
}

public sealed class PostDeliveryWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int SalesOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int LocationId { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<DeliveryLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? PostingDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class DeliveryLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int SalesOrderLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [StringLength(100)]
    public string? BatchNumber { get; init; }

    public DateTime? ExpiryDate { get; init; }
}

public sealed class PostSalesInvoiceWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int CustomerId { get; init; }

    [Required, StringLength(128)]
    public string InvoiceNumber { get; init; } = string.Empty;

    public DateOnly InvoiceDate { get; init; }

    public DateOnly DueDate { get; init; }

    public DateOnly? PostingDate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<SalesInvoiceLineWriteRequest> Lines { get; init; } = [];
}

public sealed class SalesInvoiceLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int DeliveryNoteLineId { get; init; }
}

public sealed record SalesOrderPostResponse(
    int SalesOrderId, int FirstLineId, string OrderNumber, decimal TotalValue, bool Replayed);

public sealed record DeliveryPostResponse(
    int DeliveryNoteId, int FirstLineId, string DeliveryNumber, DateOnly PostingDate,
    int DeliveredQuantity, decimal CostOfGoodsSold, string? JournalNumber, bool Replayed);

public sealed record SalesInvoicePostResponse(
    int SalesInvoiceId, string InvoiceNumber, DateOnly PostingDate,
    decimal TotalValue, decimal OpenValue, string? JournalNumber, bool Replayed);

public sealed record SalesOrderResponse(
    int Id, int CompanyId, int CustomerId, string OrderNumber, DateOnly OrderDate,
    string? Notes, IReadOnlyList<SalesOrderLineResponse> Lines);

public sealed record SalesOrderLineResponse(
    int Id, int LineNumber, int ItemId, int Quantity, decimal UnitPrice, decimal NetValue);

public sealed record SalesOrderProgressResponse(
    int SalesOrderId, long OrderedQuantity, long DeliveredQuantity, long OutstandingQuantity,
    IReadOnlyList<SalesOrderLineProgressResponse> Lines);

public sealed record SalesOrderLineProgressResponse(
    int LineId, int ItemId, int OrderedQuantity, int DeliveredQuantity, int OutstandingQuantity);


public sealed class PostQuotationWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int CustomerId { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<QuotationLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? QuotationDate { get; init; }

    public DateOnly? ValidUntil { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class QuotationLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; init; }
}

public sealed record QuotationPostResponse(
    int QuotationId, string QuotationNumber, decimal TotalValue, DateOnly ValidUntil, bool Replayed);

public sealed record QuotationConversionResponse(
    int QuotationId, int SalesOrderId, string OrderNumber, decimal TotalValue, bool Replayed);

public sealed record QuotationResponse(
    int Id, int CompanyId, int CustomerId, string QuotationNumber, string Status,
    DateOnly QuotationDate, DateOnly ValidUntil, int? SalesOrderId,
    IReadOnlyList<QuotationLineResponse> Lines);

public sealed record QuotationLineResponse(
    int Id, int LineNumber, int ItemId, int Quantity, decimal UnitPrice, decimal NetValue);
