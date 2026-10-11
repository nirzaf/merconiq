using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Builds customer receipts from persisted checkout rows. Reads are scoped to the requested company
/// and the current tenant, and a sale outside that scope is reported as absent rather than leaked.
/// </summary>
public sealed class PosReceiptService(InventoryDbContext context) : IPosReceiptService
{
    /// <inheritdoc />
    public async Task<PosReceipt?> GetSaleReceiptAsync(
        int companyId,
        int saleId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var sale = await context.PosSales
            .Include(candidate => candidate.Register)
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Tenders)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == saleId && candidate.CompanyId == companyId,
                cancellationToken);
        if (sale is null)
            return null;

        var company = await context.Companies.SingleAsync(
            candidate => candidate.Id == companyId, cancellationToken);

        var itemIds = sale.Lines.Select(line => line.ItemId).Distinct().ToList();
        var items = await context.Items
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var lines = sale.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line =>
            {
                // Items are shared masters; a missing row still prints the sold quantity and value.
                var item = items.GetValueOrDefault(line.ItemId);
                return new PosReceiptLine(
                    item?.ItemCode ?? $"ITEM-{line.ItemId}",
                    item?.Description ?? "Item",
                    line.Quantity,
                    line.UnitPrice,
                    line.LineValue);
            })
            .ToList();

        var tenders = sale.Tenders
            .OrderBy(tender => tender.LineNumber)
            .Select(tender => new PosReceiptTender(MethodName(tender.Method), tender.Amount))
            .ToList();

        return new PosReceipt(
            companyId,
            sale.Id,
            company.LegalName,
            company.TradingName,
            sale.Register.Name,
            sale.ShiftId,
            sale.SoldAtUtc,
            sale.SoldBy,
            company.CurrencyScale ?? 2,
            sale.TotalValue,
            lines,
            tenders);
    }

    private static string MethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.Card => "Card",
        _ => method.ToString()
    };
}
