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
/// Customer credit limit and open exposure. Reads need View; setting or clearing a limit needs Approve,
/// the same ceiling that approves credit-limit overrides on sales invoices.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/customers/{customerId:int}/credit")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class CustomerCreditController(
    ICustomerCreditService credit,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>The customer's limit, open receivable and headroom.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetExposure(
        int companyId,
        int customerId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var exposure = await credit.GetExposureAsync(companyId, customerId, cancellationToken);
        return Ok(ApiResponse<CustomerCreditResponse>.CreateSuccess(ToResponse(exposure)));
    }

    /// <summary>Set or clear the customer's credit limit; a reason is required and the change is audited.</summary>
    [HttpPut]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetCreditLimit(
        int companyId,
        int customerId,
        [FromBody] SetCustomerCreditLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A credit-limit body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var exposure = await credit.SetCreditLimitAsync(
            companyId, customerId, request.CreditLimit, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<CustomerCreditResponse>.CreateSuccess(ToResponse(exposure)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static CustomerCreditResponse ToResponse(CustomerCreditExposure exposure) => new(
        exposure.CustomerId,
        exposure.CustomerCode,
        exposure.CustomerName,
        exposure.CreditLimit,
        exposure.OpenReceivable,
        exposure.Available);
}

/// <summary>Credit-limit change; a null limit clears the limit. The reason is recorded with the change.</summary>
public sealed class SetCustomerCreditLimitRequest
{
    public decimal? CreditLimit { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Credit position for one customer; Available is null when no limit is set.</summary>
public sealed record CustomerCreditResponse(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal? CreditLimit,
    decimal OpenReceivable,
    decimal? Available);
