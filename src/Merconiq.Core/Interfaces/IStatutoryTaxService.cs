using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IStatutoryTaxService
{
    CountryTaxProfile GetCountryTaxProfile(string countryCode);

    Task<StatutoryInvoiceExport> GenerateStatutoryInvoiceExportAsync(
        int companyId,
        int invoiceId,
        string countryCode,
        CancellationToken cancellationToken = default);

    Task<TaxReturnSummary> GenerateTaxReturnSummaryAsync(
        int companyId,
        int fiscalPeriodId,
        CancellationToken cancellationToken = default);
}
