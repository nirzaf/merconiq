namespace Merconiq.Core.Models;

public sealed record CountryTaxProfile(
    string CountryCode,
    string CountryName,
    decimal StandardVatRate,
    decimal ReducedVatRate,
    decimal ZeroVatRate,
    bool RequiresCryptographicHash,
    string CurrencyCode);

public sealed record StatutoryInvoiceExport(
    int InvoiceId,
    string InvoiceNumber,
    DateOnly IssueDate,
    string SellerLegalName,
    string SellerTaxId,
    string BuyerLegalName,
    string BuyerTaxId,
    string CurrencyCode,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string CryptographicHash,
    IReadOnlyList<StatutoryInvoiceLineExport> Lines,
    IReadOnlyList<TaxCategoryBreakdown> TaxCategories);

public sealed record StatutoryInvoiceLineExport(
    int LineId,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal TaxRate,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record TaxCategoryBreakdown(
    string CategoryName,
    decimal Rate,
    decimal TaxableAmount,
    decimal TaxAmount);

public sealed record TaxReturnSummary(
    int CompanyId,
    int FiscalPeriodId,
    decimal StandardRateSales,
    decimal StandardOutputTax,
    decimal ReducedRateSales,
    decimal ReducedOutputTax,
    decimal ZeroRatedSales,
    decimal ExemptSales,
    decimal ReverseChargePurchases,
    decimal TotalOutputTax,
    decimal RecoverableInputTax,
    decimal NetTaxPayable);
