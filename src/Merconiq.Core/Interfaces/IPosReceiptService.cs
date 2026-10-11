using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>Reads committed point-of-sale checkouts as receipts. Read-only; never writes sales or journals.</summary>
public interface IPosReceiptService
{
    /// <summary>
    /// Builds the receipt for one sale of the company, or returns <see langword="null"/> when the sale
    /// does not exist in that company.
    /// </summary>
    Task<PosReceipt?> GetSaleReceiptAsync(
        int companyId,
        int saleId,
        CancellationToken cancellationToken = default);
}
