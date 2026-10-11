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
/// Posted payments against payables. Posting requires the company Post capability; payments are
/// immutable, fully allocated at commit, and settle payables exactly once.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/payments")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class PaymentsController(
    IPaymentService payments,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    private const int MaximumPageSize = 100;

    /// <summary>List posted payments, newest first.</summary>
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

        var entries = await payments.ListPaymentsAsync(companyId, (int)offset, pageSize + 1, cancellationToken);
        var hasMore = entries.Count > pageSize;
        return Ok(ApiResponse<PaymentPageResponse>.CreateSuccess(new PaymentPageResponse(
            page, pageSize, hasMore, entries.Take(pageSize).Select(ToResponse).ToArray())));
    }

    /// <summary>Get one posted payment with its allocations.</summary>
    [HttpGet("{paymentEntryId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetById(
        int companyId,
        int paymentEntryId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var payment = await payments.GetPaymentAsync(companyId, paymentEntryId, cancellationToken);
        if (payment is null)
            return NotFound(ApiResponse<object>.CreateFailure("Payment not found in this company."));
        return Ok(ApiResponse<PaymentDetailResponse>.CreateSuccess(new PaymentDetailResponse(
            ToResponse(payment),
            payment.Allocations.OrderBy(allocation => allocation.LineNumber)
                .Select(allocation => new PaymentAllocationResponse(
                    allocation.Id, allocation.LineNumber, allocation.PurchaseInvoiceId,
                    allocation.SalesInvoiceId, allocation.Amount))
                .ToArray())));
    }

    /// <summary>
    /// Post one supplier payment fully allocated to open invoices. Requires an Idempotency-Key
    /// header; committed requests replay, conflicting payloads are rejected, and over-allocation
    /// against open balances fails even under concurrency.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post(
        int companyId,
        [FromBody] PostPaymentWriteRequest request,
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
                "Idempotency-Key is required for payment posting."));

        var result = await payments.PostPaymentAsync(
            new PostPaymentRequest(
                companyId,
                request.PaymentType,
                request.Method,
                requestKey,
                request.Amount,
                request.Allocations
                    .Select(allocation => new PostPaymentAllocation(
                        allocation.PurchaseInvoiceId, allocation.Amount))
                    .ToArray(),
                SupplierId: request.SupplierId,
                CustomerId: request.CustomerId,
                PostingDate: request.PostingDate,
                IsAdvance: request.IsAdvance,
                FromAccountId: request.FromAccountId,
                ToAccountId: request.ToAccountId,
                Reference: request.Reference,
                Notes: request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<PaymentPostResponse>.CreateSuccess(new PaymentPostResponse(
            result.PaymentEntryId,
            result.PostingDate,
            result.Amount,
            result.AllocatedValue,
            result.JournalNumber,
            result.Replayed)));
    }

    private static PaymentResponse ToResponse(PaymentEntry payment) => new(
        payment.Id,
        payment.CompanyId,
        payment.PaymentType.ToString(),
        payment.Method.ToString(),
        payment.SupplierId,
        payment.CustomerId,
        payment.Amount,
        payment.CurrencyCode,
        payment.PostingDate,
        payment.Reference,
        payment.PostedAtUtc,
        payment.PostedBy);
}

/// <summary>Supplier-payment posting request.</summary>
public sealed class PostPaymentWriteRequest
{
    public int CompanyId { get; init; }

    public PaymentEntryType PaymentType { get; init; } = PaymentEntryType.Pay;

    public PaymentMethod Method { get; init; } = PaymentMethod.BankTransfer;

    [Range(1, int.MaxValue)]
    public int? SupplierId { get; init; }

    [Range(1, int.MaxValue)]
    public int? CustomerId { get; init; }

    /// <summary>Post as unallocated money settled against the party.</summary>
    public bool IsAdvance { get; init; }

    /// <summary>Internal transfers only: the account money leaves.</summary>
    [Range(1, int.MaxValue)]
    public int? FromAccountId { get; init; }

    /// <summary>Internal transfers only: the account money arrives in.</summary>
    [Range(1, int.MaxValue)]
    public int? ToAccountId { get; init; }

    [Range(0.000001, double.MaxValue)]
    public decimal Amount { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PaymentAllocationWriteRequest> Allocations { get; init; } = [];

    public DateOnly? PostingDate { get; init; }

    [StringLength(128)]
    public string? Reference { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>One requested allocation against an open invoice.</summary>
public sealed class PaymentAllocationWriteRequest
{
    [Range(1, int.MaxValue)]
    public int PurchaseInvoiceId { get; init; }

    [Range(0.000001, double.MaxValue)]
    public decimal Amount { get; init; }
}

/// <summary>A bounded page of posted payments.</summary>
public sealed record PaymentPageResponse(
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<PaymentResponse> Items);

/// <summary>Payment header fields exposed by the API.</summary>
public sealed record PaymentResponse(
    int Id,
    int CompanyId,
    string PaymentType,
    string Method,
    int? SupplierId,
    int? CustomerId,
    decimal Amount,
    string CurrencyCode,
    DateOnly PostingDate,
    string? Reference,
    DateTime PostedAtUtc,
    string PostedBy);

/// <summary>One payment with its allocations.</summary>
public sealed record PaymentDetailResponse(
    PaymentResponse Payment,
    IReadOnlyList<PaymentAllocationResponse> Allocations);

/// <summary>One immutable payment allocation.</summary>
public sealed record PaymentAllocationResponse(
    int Id,
    int LineNumber,
    int? PurchaseInvoiceId,
    int? SalesInvoiceId,
    decimal Amount);

/// <summary>The committed-or-replayed payment outcome.</summary>
public sealed record PaymentPostResponse(
    int PaymentEntryId,
    DateOnly PostingDate,
    decimal Amount,
    decimal AllocatedValue,
    string? JournalNumber,
    bool Replayed);
