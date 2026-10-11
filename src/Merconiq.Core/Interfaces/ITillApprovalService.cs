using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Reviews shift closes whose cash variance exceeded the register's approval threshold. Approval posts the
/// over/short journal exactly as an immediate close would; rejection returns the shift to the cashier for a recount.
/// </summary>
public interface ITillApprovalService
{
    /// <summary>Approves a pending close, posting its over/short journal once and closing the shift.</summary>
    Task<TillApprovalResult> ApproveCloseAsync(
        int companyId,
        int shiftId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a pending close back to the cashier with a mandatory reason; the shift reopens for sales.</summary>
    Task<TillApprovalResult> RejectCloseAsync(
        int companyId,
        int shiftId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or clears a register's approval threshold; null means closes never wait for review.</summary>
    Task<PosRegister> SetVarianceThresholdAsync(
        int companyId,
        int registerId,
        decimal? threshold,
        string actor,
        CancellationToken cancellationToken = default);
}
