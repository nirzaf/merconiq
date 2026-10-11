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
/// Point-of-sale operation: registers, cashier shifts, idempotent checkouts with tender splits
/// and exact COGS, bounded refunds, and shift close with cash reconciliation.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/pos")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class PosController(
    IPosService pos,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create a register bound to a company location.</summary>
    [HttpPost("registers")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateRegister(
        int companyId,
        [FromBody] PosRegisterWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var register = await pos.CreateRegisterAsync(
            companyId, request.LocationId, request.Name, actor, cancellationToken);
        return Ok(ApiResponse<PosRegisterResponse>.CreateSuccess(new PosRegisterResponse(
            register.Id, register.CompanyId, register.LocationId, register.Name, register.IsActive)));
    }

    /// <summary>Open a cashier shift with its drawer float.</summary>
    [HttpPost("shifts/open")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> OpenShift(
        int companyId,
        [FromBody] PosShiftOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var shift = await pos.OpenShiftAsync(
            new OpenPosShiftRequest(companyId, request.RegisterId, request.OpeningFloat), actor, cancellationToken);
        return Ok(ApiResponse<PosShiftResponse>.CreateSuccess(ToResponse(shift)));
    }

    /// <summary>
    /// Post one checkout on an open shift. Requires an Idempotency-Key header; committed requests
    /// replay and conflicting payloads are rejected. Issues valued stock and posts the tender,
    /// revenue and COGS journals atomically.
    /// </summary>
    [HttpPost("sales")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostSale(
        int companyId,
        [FromBody] PosSaleWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();
        var requestKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(requestKey))
            return BadRequest(ApiResponse<object>.CreateFailure(
                "Idempotency-Key is required for POS checkout."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var result = await pos.PostSaleAsync(
            new PostPosSaleRequest(
                companyId,
                request.RegisterId,
                request.ShiftId,
                requestKey,
                request.Lines.Select(line => new PostPosSaleLine(line.ItemId, line.Quantity, line.UnitPrice)).ToArray(),
                request.Tenders.Select(tender => new PostPosTender(tender.Method, tender.Amount)).ToArray(),
                request.CustomerId),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<PosSalePostResponse>.CreateSuccess(new PosSalePostResponse(
            result.PosSaleId, result.TotalValue, result.CostOfGoodsSold, result.Replayed)));
    }

    /// <summary>Post one refund against a prior checkout; quantities bounded by sold-minus-refunded.</summary>
    [HttpPost("refunds")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostRefund(
        int companyId,
        [FromBody] PosRefundWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();
        var requestKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(requestKey))
            return BadRequest(ApiResponse<object>.CreateFailure(
                "Idempotency-Key is required for POS refunds."));

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var result = await pos.PostRefundAsync(
            new PostPosRefundRequest(
                companyId,
                request.RegisterId,
                request.ShiftId,
                request.OriginalPosSaleId,
                requestKey,
                request.Reason,
                request.Lines.Select(line => new PostPosRefundLine(
                    line.PosSaleLineId, line.Quantity, line.Disposition)).ToArray(),
                request.PostingDate),
            mutationScope,
            actor,
            cancellationToken);
        return Ok(ApiResponse<PosRefundPostResponse>.CreateSuccess(new PosRefundPostResponse(
            result.PosRefundId, result.TotalValue, result.RestockedValue, result.Replayed)));
    }

    /// <summary>Close one open shift; the counted-cash variance posts to cash over/short once.</summary>
    [HttpPost("shifts/{shiftId:int}/close")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CloseShift(
        int companyId,
        int shiftId,
        [FromBody] PosShiftCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var result = await pos.CloseShiftAsync(
            new ClosePosShiftRequest(companyId, shiftId, request.CountedCash), actor, cancellationToken);
        return Ok(ApiResponse<PosShiftCloseResponse>.CreateSuccess(new PosShiftCloseResponse(
            result.PosShiftId,
            result.OpeningFloat,
            result.CashSales,
            result.CashRefunds,
            result.ExpectedCash,
            result.CountedCash,
            result.OverShortValue,
            result.OverShortJournalNumber)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static PosShiftResponse ToResponse(PosShift shift) => new(
        shift.Id,
        shift.CompanyId,
        shift.RegisterId,
        shift.Status.ToString(),
        shift.OpeningFloat,
        shift.OpenedAtUtc,
        shift.OpenedBy,
        shift.ClosedAtUtc,
        shift.ClosedBy,
        shift.CountedCash,
        shift.ExpectedCash,
        shift.OverShortValue);
}

public sealed class PosRegisterWriteRequest
{
    [Range(1, int.MaxValue)]
    public int LocationId { get; init; }

    [Required, StringLength(100)]
    public string Name { get; init; } = string.Empty;
}

public sealed class PosShiftOpenRequest
{
    [Range(1, int.MaxValue)]
    public int RegisterId { get; init; }

    [Range(0, double.MaxValue)]
    public decimal OpeningFloat { get; init; }
}

public sealed class PosSaleWriteRequest
{
    [Range(1, int.MaxValue)]
    public int RegisterId { get; init; }

    [Range(1, int.MaxValue)]
    public int ShiftId { get; init; }

    [Range(1, int.MaxValue)]
    public int? CustomerId { get; init; }

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PosSaleLineWriteRequest> Lines { get; init; } = [];

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PosTenderWriteRequest> Tenders { get; init; } = [];
}

public sealed class PosSaleLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; init; }
}

public sealed class PosTenderWriteRequest
{
    public PaymentMethod Method { get; init; } = PaymentMethod.Cash;

    [Range(0.000001, double.MaxValue)]
    public decimal Amount { get; init; }
}

public sealed class PosRefundWriteRequest
{
    [Range(1, int.MaxValue)]
    public int RegisterId { get; init; }

    [Range(1, int.MaxValue)]
    public int ShiftId { get; init; }

    [Range(1, int.MaxValue)]
    public int OriginalPosSaleId { get; init; }

    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    [MinLength(1), MaxLength(200)]
    public IReadOnlyList<PosRefundLineWriteRequest> Lines { get; init; } = [];

    public DateOnly? PostingDate { get; init; }
}

public sealed class PosRefundLineWriteRequest
{
    [Range(1, int.MaxValue)]
    public int PosSaleLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    public StockReturnDisposition Disposition { get; init; } = StockReturnDisposition.Restockable;
}

public sealed class PosShiftCloseRequest
{
    [Range(0, double.MaxValue)]
    public decimal CountedCash { get; init; }
}

public sealed record PosRegisterResponse(
    int Id, int CompanyId, int LocationId, string Name, bool IsActive);

public sealed record PosShiftResponse(
    int Id, int CompanyId, int RegisterId, string Status, decimal OpeningFloat,
    DateTime OpenedAtUtc, string OpenedBy, DateTime? ClosedAtUtc, string? ClosedBy,
    decimal? CountedCash, decimal? ExpectedCash, decimal? OverShortValue);

public sealed record PosSalePostResponse(
    int PosSaleId, decimal TotalValue, decimal CostOfGoodsSold, bool Replayed);

public sealed record PosRefundPostResponse(
    int PosRefundId, decimal TotalValue, decimal RestockedValue, bool Replayed);

public sealed record PosShiftCloseResponse(
    int PosShiftId, decimal OpeningFloat, decimal CashSales, decimal CashRefunds,
    decimal ExpectedCash, decimal CountedCash, decimal OverShortValue, string? OverShortJournalNumber);
