using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class BankingFeedService(
    InventoryDbContext context,
    ILogger<BankingFeedService> logger) : IBankingFeedService
{
    public async Task<BankStatementFile> ImportStatementAsync(
        ImportBankStatementRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.BankAccountId <= 0)
            throw new FinancePostingValidationException("CompanyId and BankAccountId are required.");
        if (request.Lines.Count == 0)
            throw new FinancePostingValidationException("At least one statement line is required.");

        var file = new BankStatementFile
        {
            CompanyId = request.CompanyId,
            BankAccountId = request.BankAccountId,
            FileName = request.FileName.Trim(),
            Format = request.Format,
            StatementDate = request.StatementDate,
            OpeningBalance = request.OpeningBalance,
            ClosingBalance = request.ClosingBalance,
            Status = BankStatementFileStatus.Imported
        };

        foreach (var line in request.Lines)
        {
            file.Transactions.Add(new BankStatementTransaction
            {
                TransactionDate = line.TransactionDate,
                Reference = line.Reference.Trim(),
                Payee = line.Payee.Trim(),
                Amount = line.Amount,
                IsMatched = false
            });
        }

        context.BankStatementFiles.Add(file);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Imported bank statement {FileName} with {Count} transactions", file.FileName, file.Transactions.Count);
        return file;
    }

    public async Task<CashFlowStatementReport> GenerateCashFlowStatementAsync(
        int companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");

        // Operating activities: Customer receipts vs Supplier/Operating payments
        var payments = await context.PaymentEntries
            .Where(p => p.CompanyId == companyId && p.PostingDate >= fromDate && p.PostingDate <= toDate)
            .ToListAsync(cancellationToken);

        var receiptsTotal = payments.Where(p => p.PaymentType == PaymentEntryType.Receive).Sum(p => p.Amount);
        var paymentsTotal = payments.Where(p => p.PaymentType == PaymentEntryType.Pay).Sum(p => p.Amount);

        var operatingCashFlow = receiptsTotal - paymentsTotal;

        // Investing activities: Fixed asset purchases or disposals
        var assetCapitalizations = await context.FixedAssets
            .Where(a => a.CompanyId == companyId && a.AcquisitionDate >= fromDate && a.AcquisitionDate <= toDate)
            .SumAsync(a => (decimal?)a.CostAmount, cancellationToken) ?? 0m;
        var investingCashFlow = -assetCapitalizations;

        // Financing activities
        var financingCashFlow = 0m;

        var netChange = operatingCashFlow + investingCashFlow + financingCashFlow;

        return new CashFlowStatementReport(
            companyId,
            fromDate,
            toDate,
            NetIncome: operatingCashFlow,
            OperatingCashFlow: operatingCashFlow,
            InvestingCashFlow: investingCashFlow,
            FinancingCashFlow: financingCashFlow,
            NetCashChange: netChange);
    }
}
