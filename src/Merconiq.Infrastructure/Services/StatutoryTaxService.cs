using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

public sealed class StatutoryTaxService(
    InventoryDbContext db,
    ITenantContext tenantContext) : IStatutoryTaxService
{
    private static readonly Dictionary<string, CountryTaxProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SA"] = new("SA", "Saudi Arabia (ZATCA)", 0.15m, 0.05m, 0.00m, true, "SAR"),
        ["AE"] = new("AE", "United Arab Emirates (FTA)", 0.05m, 0.00m, 0.00m, false, "AED"),
        ["GB"] = new("GB", "United Kingdom (HMRC)", 0.20m, 0.05m, 0.00m, false, "GBP"),
        ["US"] = new("US", "United States", 0.0825m, 0.00m, 0.00m, false, "USD")
    };

    public CountryTaxProfile GetCountryTaxProfile(string countryCode)
    {
        if (Profiles.TryGetValue(countryCode, out var profile))
            return profile;

        return new CountryTaxProfile(countryCode.ToUpperInvariant(), "Standard International", 0.10m, 0.05m, 0.00m, false, "USD");
    }

    public async Task<StatutoryInvoiceExport> GenerateStatutoryInvoiceExportAsync(
        int companyId,
        int invoiceId,
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == companyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var invoice = await db.SalesInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CompanyId == companyId && i.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Sales invoice not found.");

        var customer = await db.Customers.FirstOrDefaultAsync(
            c => c.Id == invoice.CustomerId && c.CompanyId == companyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");

        var profile = GetCountryTaxProfile(countryCode);
        var subtotal = invoice.TotalValue;
        var taxAmount = Math.Round(subtotal * profile.StandardVatRate, company.CurrencyScale ?? 2, MidpointRounding.ToEven);
        var totalAmount = subtotal + taxAmount;

        var linesExport = invoice.Lines.Select(line =>
        {
            var unitPrice = line.Quantity > 0
                ? Math.Round(line.RevenueValue / line.Quantity, company.CurrencyScale ?? 2, MidpointRounding.ToEven)
                : 0m;
            var lineTax = Math.Round(line.RevenueValue * profile.StandardVatRate, company.CurrencyScale ?? 2, MidpointRounding.ToEven);
            return new StatutoryInvoiceLineExport(
                line.Id,
                $"Item #{line.ItemId ?? line.LineNumber}",
                line.Quantity,
                unitPrice,
                line.RevenueValue,
                profile.StandardVatRate,
                lineTax,
                line.RevenueValue + lineTax);
        }).ToList();

        var categories = new List<TaxCategoryBreakdown>
        {
            new("Standard Rate", profile.StandardVatRate, subtotal, taxAmount)
        };

        // Calculate cryptographic tamper-evident SHA-256 hash (e.g. ZATCA e-invoice hash)
        var hashPayload = $"{invoice.InvoiceNumber}|{invoice.InvoiceDate:O}|{company.LegalName}|{subtotal}|{taxAmount}|{totalAmount}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload));
        var invoiceHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new StatutoryInvoiceExport(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            company.LegalName,
            "TAX-" + company.Code,
            customer.Name,
            customer.CustomerCode,
            company.BaseCurrency,
            subtotal,
            taxAmount,
            totalAmount,
            invoiceHash,
            linesExport,
            categories);
    }

    public async Task<TaxReturnSummary> GenerateTaxReturnSummaryAsync(
        int companyId,
        int fiscalPeriodId,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == companyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var period = await db.FiscalPeriods.FirstOrDefaultAsync(
            p => p.Id == fiscalPeriodId && p.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Fiscal period not found.");

        var salesInvoices = await db.SalesInvoices
            .Where(i => i.CompanyId == companyId &&
                        i.TenantId == tenantContext.TenantId &&
                        i.PostingDate >= period.StartDate &&
                        i.PostingDate <= period.EndDate)
            .ToListAsync(cancellationToken);

        var purchaseInvoices = await db.PurchaseInvoices
            .Where(i => i.CompanyId == companyId &&
                        i.TenantId == tenantContext.TenantId &&
                        i.PostingDate >= period.StartDate &&
                        i.PostingDate <= period.EndDate)
            .ToListAsync(cancellationToken);

        var profile = GetCountryTaxProfile("SA");
        var scale = company.CurrencyScale ?? 2;

        decimal standardSales = salesInvoices.Sum(i => i.TotalValue);
        decimal standardTax = Math.Round(standardSales * profile.StandardVatRate, scale, MidpointRounding.ToEven);
        decimal inputPurchases = purchaseInvoices.Sum(p => p.TotalValue);
        decimal inputTax = Math.Round(inputPurchases * profile.StandardVatRate, scale, MidpointRounding.ToEven);
        decimal netPayable = standardTax - inputTax;

        return new TaxReturnSummary(
            companyId,
            fiscalPeriodId,
            standardSales,
            standardTax,
            0,
            0,
            0,
            0,
            0,
            standardTax,
            inputTax,
            netPayable);
    }
}
