using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface ILandedCostService
{
    Task<LandedCostVoucher> CreateVoucherAsync(CreateLandedCostVoucherRequest request, string actor, CancellationToken cancellationToken = default);
    Task<LandedCostVoucherResult> PostLandedCostVoucherAsync(int voucherId, string actor, CancellationToken cancellationToken = default);
}
