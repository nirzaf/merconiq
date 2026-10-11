using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>
/// Company posting-policy configuration: maps named account roles (Inventory, GRNI, AP, AR,
/// Bank, COGS, Sales, and so on) to company-owned chart accounts. Mappings are configuration
/// only; the posting kernel still validates every mapped account at posting time.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/account-posting-mappings")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class AccountPostingMappingsController(
    InventoryDbContext db,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List account-role mappings for one authorized company.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetAll(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var mappings = await db.CompanyAccountMappings
            .Where(mapping => mapping.CompanyId == companyId)
            .OrderBy(mapping => mapping.Role)
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AccountMappingResponse>>.CreateSuccess(
            mappings.Select(ToResponse).ToArray()));
    }

    /// <summary>Create or replace the account mapping for one role.</summary>
    [HttpPut]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Upsert(
        int companyId,
        [FromBody] AccountMappingWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();

        if (!await db.Companies.AnyAsync(company => company.Id == companyId && company.IsActive, cancellationToken))
            return NotFound(ApiResponse<object>.CreateFailure("Active company not found."));

        // The mapped account must be an active, posting-capable account of this company at
        // configuration time; the posting policy revalidates at every resolution.
        var account = await db.ChartOfAccounts.SingleOrDefaultAsync(
            candidate => candidate.Id == request.ChartOfAccountId && candidate.CompanyId == companyId,
            cancellationToken);
        if (account is null)
            return BadRequest(ApiResponse<object>.CreateFailure(
                "ChartOfAccountId must identify a chart account in this company."));
        if (!account.IsActive || account.IsGroupAccount)
            return BadRequest(ApiResponse<object>.CreateFailure(
                "The mapped account must be active and must not be a group account."));

        var mapping = await db.CompanyAccountMappings.SingleOrDefaultAsync(
            candidate => candidate.CompanyId == companyId && candidate.Role == request.Role,
            cancellationToken);
        if (mapping is null)
        {
            mapping = new CompanyAccountMapping { CompanyId = companyId, Role = request.Role };
            db.CompanyAccountMappings.Add(mapping);
        }

        mapping.ChartOfAccountId = request.ChartOfAccountId;
        mapping.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ApiResponse<AccountMappingResponse>.CreateSuccess(ToResponse(mapping)));
    }

    private static AccountMappingResponse ToResponse(CompanyAccountMapping mapping) => new(
        mapping.Id,
        mapping.CompanyId,
        mapping.Role.ToString(),
        mapping.ChartOfAccountId,
        mapping.IsActive);
}

/// <summary>Account-mapping upsert fields; ownership is taken from the route and session.</summary>
public sealed class AccountMappingWriteRequest
{
    public int CompanyId { get; init; }

    [Required]
    public AccountPostingRole Role { get; init; }

    [Range(1, int.MaxValue)]
    public int ChartOfAccountId { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>One company account-role mapping.</summary>
public sealed record AccountMappingResponse(
    int Id,
    int CompanyId,
    string Role,
    int ChartOfAccountId,
    bool IsActive);
