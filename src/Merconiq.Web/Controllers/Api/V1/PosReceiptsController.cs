using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>
/// Printable point-of-sale receipts for committed checkouts. Returns plain monospace text suitable for
/// screens and thermal printers; the receipt is read-only and reflects the persisted sale exactly.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/pos/sales/{saleId:int}/receipt")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class PosReceiptsController(
    IPosReceiptService receipts,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Plain-text receipt for one committed sale in the authorized company.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetReceipt(
        int companyId,
        int saleId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var receipt = await receipts.GetSaleReceiptAsync(companyId, saleId, cancellationToken);
        if (receipt is null)
            return NotFound(ApiResponse<object>.CreateFailure("POS sale not found in this company."));

        return Content(PosReceiptFormatter.Format(receipt), "text/plain; charset=utf-8");
    }
}
