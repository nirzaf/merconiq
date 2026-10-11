using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Order-to-cash posting. A sales order is a commitment only. A delivery issues stock through
/// the stock ledger at moving-average cost and books the exact issued value as COGS. A sales
/// invoice matches delivered lines exactly once and recognizes revenue and the receivable.
/// Each operation commits once per request key; retries replay and conflicting payloads are
/// rejected.
/// </summary>
public sealed class SalesService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IStockService stockService,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    IDocumentNumberService documentNumbers,
    ILogger<SalesService> logger) : ISalesService
{
    public const string SalesOrderRequestKeyIndex = "UX_SalesOrders_RequestKey";
    public const string DeliveryRequestKeyIndex = "UX_DeliveryNotes_RequestKey";
    public const string SalesInvoiceRequestKeyIndex = "UX_SalesInvoices_RequestKey";
    /// <summary>One invoice line per delivered line, ever.</summary>
    public const string DeliveryLineMatchIndex = "UX_SalesInvoiceLines_DeliveryLine";

    public const string OrderDocumentType = "SalesOrder";
    public const string DeliveryDocumentType = "DeliveryNote";
    public const string InvoiceDocumentType = "SalesInvoice";

    private const int MaximumLines = 200;

    /// <inheritdoc />
    public async Task<SalesOrderResult> PostSalesOrderAsync(
        PostSalesOrderRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateOrderRequest(request);
        var normalized = request with
        {
            RequestKey = request.RequestKey.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("order\u001f").Append(request.CompanyId).Append('\u001f').Append(request.CustomerId)
                .Append('\u001f').Append(normalized.RequestKey).Append('\u001f')
                .Append((request.OrderDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty)
                .Append('\u001f').Append(normalized.Notes ?? string.Empty);
            foreach (var line in request.Lines)
            {
                h.Append(line.ItemId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append(line.UnitPrice.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f');
            }
        });

        SalesOrderResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-order:{normalized.CompanyId}", cancellationToken);
                var committed = await context.SalesOrders.SingleOrDefaultAsync(
                    order => order.CompanyId == normalized.CompanyId && order.RequestKey == normalized.RequestKey,
                    cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This sales-order request key was already committed with a different payload.");
                    result = new SalesOrderResult(committed.Id,
                        committed.Lines.OrderBy(line => line.LineNumber).First().Id,
                        committed.OrderNumber, committed.Lines.Sum(line => line.NetValue), Replayed: true);
                    return;
                }

                var company = await RequireCompanyAsync(normalized.CompanyId, cancellationToken);
                var customer = await context.Customers.SingleOrDefaultAsync(
                        candidate => candidate.Id == normalized.CustomerId && candidate.CompanyId == normalized.CompanyId,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Customer not found in this company.");

                var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToList();
                var items = await context.Items
                    .Where(item => itemIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, cancellationToken);
                var orderDate = request.OrderDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var scale = company.CurrencyScale ?? 2;

                var order = new SalesOrder
                {
                    CompanyId = normalized.CompanyId,
                    CustomerId = customer.Id,
                    OrderDate = orderDate,
                    Notes = normalized.Notes,
                    RequestKey = normalized.RequestKey,
                    RequestHash = requestHash,
                    CurrencyScale = scale,
                    ApprovedAtUtc = DateTime.UtcNow,
                    ApprovedBy = actor.Trim()
                };
                foreach (var line in request.Lines)
                {
                    if (!items.ContainsKey(line.ItemId))
                        throw new FinancePostingValidationException($"Item {line.ItemId} does not exist.");
                    if (line.UnitPrice < 0)
                        throw new FinancePostingValidationException("Unit prices must not be negative.");
                    var roundedPrice = decimal.Round(line.UnitPrice, scale, MidpointRounding.AwayFromZero);
                    if (roundedPrice != line.UnitPrice)
                        throw new FinancePostingValidationException(
                            $"Unit prices must already be rounded to {scale} decimal places.");
                    order.Lines.Add(new SalesOrderLine
                    {
                        CompanyId = normalized.CompanyId,
                        LineNumber = order.Lines.Count + 1,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = roundedPrice,
                        NetValue = decimal.Round(line.Quantity * roundedPrice, scale, MidpointRounding.AwayFromZero)
                    });
                }

                order.OrderNumber = await documentNumbers.AllocateAsync(
                    normalized.CompanyId, OrderDocumentType, orderDate.Year, "SO", cancellationToken);
                context.SalesOrders.Add(order);
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

                logger.LogInformation("Sales order {OrderNumber} committed for company {CompanyId}",
                    order.OrderNumber, normalized.CompanyId);
                result = new SalesOrderResult(order.Id,
                    order.Lines.OrderBy(line => line.LineNumber).First().Id,
                    order.OrderNumber, order.Lines.Sum(line => line.NetValue), Replayed: false);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.SalesOrders.AnyAsync(
                order => order.CompanyId == normalized.CompanyId && order.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<DeliveryResult> PostDeliveryAsync(
        PostDeliveryRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateDeliveryRequest(request);
        var normalized = request with
        {
            RequestKey = request.RequestKey.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("delivery\u001f").Append(request.CompanyId).Append('\u001f').Append(request.SalesOrderId)
                .Append('\u001f').Append(request.LocationId).Append('\u001f').Append(normalized.RequestKey)
                .Append('\u001f')
                .Append((request.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty)
                .Append('\u001f').Append(normalized.Notes ?? string.Empty);
            foreach (var line in request.Lines)
            {
                h.Append(line.SalesOrderLineId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append(line.BatchNumber ?? string.Empty).Append('\u001f')
                    .Append(line.ExpiryDate?.ToString("O") ?? string.Empty).Append('\u001f');
            }
        });

        DeliveryResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-delivery:{normalized.CompanyId}", cancellationToken);
                var committed = await context.DeliveryNotes
                    .Include(delivery => delivery.Lines)
                    .SingleOrDefaultAsync(
                        delivery => delivery.CompanyId == normalized.CompanyId && delivery.RequestKey == normalized.RequestKey,
                        cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This delivery request key was already posted with a different payload.");
                    var committedCogs = await CostOfGoodsForDeliveryAsync(committed, cancellationToken);
                    result = new DeliveryResult(committed.Id,
                        committed.Lines.OrderBy(line => line.LineNumber).First().Id,
                        committed.DeliveryNumber, committed.PostingDate,
                        committed.Lines.Sum(line => line.Quantity), committedCogs.Total,
                        committedCogs.JournalNumber, Replayed: true);
                    return;
                }

                result = await DeliverCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.DeliveryNotes.AnyAsync(
                delivery => delivery.CompanyId == normalized.CompanyId && delivery.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<SalesInvoiceResult> PostSalesInvoiceAsync(
        PostSalesInvoiceRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateInvoiceRequest(request);
        var normalized = request with
        {
            InvoiceNumber = request.InvoiceNumber.Trim(),
            RequestKey = request.RequestKey.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("sales-invoice\u001f").Append(request.CompanyId).Append('\u001f').Append(request.CustomerId)
                .Append('\u001f').Append(normalized.InvoiceNumber).Append('\u001f').Append(normalized.RequestKey)
                .Append('\u001f')
                .Append(request.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\u001f')
                .Append(request.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\u001f')
                .Append((request.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty)
                .Append('\u001f').Append(normalized.Notes ?? string.Empty);
            foreach (var line in request.Lines)
            {
                h.Append(line.DeliveryNoteLineId).Append('\u001f');
            }
        });

        SalesInvoiceResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-invoice:{normalized.CompanyId}", cancellationToken);
                var committed = await context.SalesInvoices.SingleOrDefaultAsync(
                    invoice => invoice.CompanyId == normalized.CompanyId && invoice.RequestKey == normalized.RequestKey,
                    cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This sales-invoice request key was already posted with a different payload.");
                    result = await ToInvoiceResultAsync(committed, replayed: true, cancellationToken);
                    return;
                }

                result = await InvoiceCoreAsync(normalized, requestHash, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.SalesInvoices.AnyAsync(
                invoice => invoice.CompanyId == normalized.CompanyId && invoice.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public Task<SalesOrder?> GetSalesOrderAsync(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.SalesOrders
            .Include(order => order.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(order => order.Id == salesOrderId && order.CompanyId == companyId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SalesOrderProgress?> GetSalesOrderProgressAsync(
        int companyId,
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var order = await context.SalesOrders
            .Include(order => order.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(order => order.Id == salesOrderId && order.CompanyId == companyId, cancellationToken);
        if (order is null)
            return null;

        var lineIds = order.Lines.Select(line => line.Id).ToList();
        var delivered = await context.DeliveryNoteLines
            .Where(line => lineIds.Contains(line.SalesOrderLineId))
            .GroupBy(line => line.SalesOrderLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.LineId, row => row.Quantity, cancellationToken);

        var lines = order.Lines.Select(line => new SalesOrderLineProgress(
                line.Id,
                line.ItemId,
                line.Quantity,
                delivered.GetValueOrDefault(line.Id),
                line.Quantity - delivered.GetValueOrDefault(line.Id)))
            .ToList();
        var ordered = lines.Sum(line => (long)line.OrderedQuantity);
        var deliveredTotal = lines.Sum(line => (long)line.DeliveredQuantity);
        return new SalesOrderProgress(
            order.Id, ordered, deliveredTotal, ordered - deliveredTotal, lines);
    }

    /// <inheritdoc />
    public async Task<QuotationResult> PostQuotationAsync(
        PostQuotationRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (request.CompanyId <= 0 || request.CustomerId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId and CustomerId are required.");
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A quotation request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A quotation requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.ItemId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0)
                throw new FinancePostingValidationException(
                    "Every quotation line needs a positive item id and quantity, and a non-negative price.");
        }

        var normalized = request with { RequestKey = requestKey };
        var requestHash = ComputeHash(h =>
        {
            h.Append("quotation\u001f").Append(request.CompanyId).Append('\u001f').Append(request.CustomerId)
                .Append('\u001f').Append(normalized.RequestKey).Append('\u001f')
                .Append((request.QuotationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty)
                .Append('\u001f')
                .Append((request.ValidUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty)
                .Append('\u001f').Append(request.Notes ?? string.Empty);
            foreach (var line in normalized.Lines)
            {
                h.Append(line.ItemId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append(line.UnitPrice.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001f');
            }
        });

        QuotationResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-quotation:{normalized.CompanyId}", cancellationToken);
                var committed = await context.Quotations
                    .Include(quotation => quotation.Lines)
                    .SingleOrDefaultAsync(quotation => quotation.CompanyId == normalized.CompanyId &&
                        quotation.RequestKey == normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This quotation request key was already committed with a different payload.");
                    result = new QuotationResult(committed.Id, committed.QuotationNumber,
                        committed.Lines.Sum(line => line.NetValue), committed.ValidUntil, Replayed: true);
                    return;
                }

                var company = await RequireCompanyAsync(normalized.CompanyId, cancellationToken);
                var customer = await context.Customers.SingleOrDefaultAsync(
                        candidate => candidate.Id == normalized.CustomerId && candidate.CompanyId == normalized.CompanyId,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Customer not found in this company.");
                var itemIds = normalized.Lines.Select(line => line.ItemId).Distinct().ToList();
                var items = await context.Items
                    .Where(item => itemIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, cancellationToken);

                var quotationDate = request.QuotationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var validUntil = request.ValidUntil ?? quotationDate.AddDays(30);
                if (validUntil < quotationDate)
                    throw new FinancePostingValidationException("The validity end must not precede the quotation date.");
                var scale = company.CurrencyScale ?? 2;

                var quotation = new Quotation
                {
                    CompanyId = normalized.CompanyId,
                    CustomerId = customer.Id,
                    QuotationDate = quotationDate,
                    ValidUntil = validUntil,
                    RequestKey = normalized.RequestKey,
                    RequestHash = requestHash,
                    Notes = request.Notes,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedBy = actor.Trim()
                };
                foreach (var line in normalized.Lines)
                {
                    if (!items.ContainsKey(line.ItemId))
                        throw new FinancePostingValidationException($"Item {line.ItemId} does not exist.");
                    var roundedPrice = decimal.Round(line.UnitPrice, scale, MidpointRounding.AwayFromZero);
                    if (roundedPrice != line.UnitPrice)
                        throw new FinancePostingValidationException(
                            $"Unit prices must already be rounded to {scale} decimal places.");
                    quotation.Lines.Add(new QuotationLine
                    {
                        CompanyId = normalized.CompanyId,
                        LineNumber = quotation.Lines.Count + 1,
                        ItemId = line.ItemId,
                        Quantity = line.Quantity,
                        UnitPrice = roundedPrice,
                        NetValue = decimal.Round(line.Quantity * roundedPrice, scale, MidpointRounding.AwayFromZero)
                    });
                }

                quotation.QuotationNumber = await documentNumbers.AllocateAsync(
                    normalized.CompanyId, "Quotation", quotationDate.Year, "QT", cancellationToken);
                context.Quotations.Add(quotation);
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

                logger.LogInformation("Quotation {QuotationNumber} committed for company {CompanyId}",
                    quotation.QuotationNumber, normalized.CompanyId);
                result = new QuotationResult(quotation.Id, quotation.QuotationNumber,
                    quotation.Lines.Sum(line => line.NetValue), quotation.ValidUntil, Replayed: false);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.Quotations.AnyAsync(
                quotation => quotation.CompanyId == normalized.CompanyId && quotation.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<QuotationConversionResult> ConvertQuotationAsync(
        ConvertQuotationRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.CompanyId <= 0 || request.QuotationId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId and QuotationId are required.");

        QuotationConversionResult result = null!;
        (string? Number, DateOnly ValidUntil, int Id, int CompanyId)? expiredQuotation = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-quotation:{request.CompanyId}", cancellationToken);
                var quotation = await context.Quotations
                    .Include(candidate => candidate.Lines)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == request.QuotationId && candidate.CompanyId == request.CompanyId,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Quotation not found in this company.");

                if (quotation.Status == QuotationStatus.Accepted && quotation.SalesOrderId is { } existingOrderId)
                {
                    // Idempotent conversion: the committed order answers repeated calls.
                    var existingOrder = await context.SalesOrders.SingleAsync(
                        order => order.Id == existingOrderId, cancellationToken);
                    result = new QuotationConversionResult(quotation.Id, existingOrderId,
                        existingOrder.OrderNumber,
                        existingOrder.Lines.Sum(line => line.NetValue), Replayed: true);
                    return;
                }
                if (quotation.Status != QuotationStatus.Open)
                    throw new FinancePostingConflictException(
                        $"A {quotation.Status} quotation cannot be converted; create a new quotation.");
                if (quotation.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    // Mark the expiry and let it commit; the rejection throws after the
                    // transaction so the status transition is not rolled back.
                    quotation.Status = QuotationStatus.Expired;
                    await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
                    expiredQuotation = (quotation.QuotationNumber, quotation.ValidUntil, quotation.Id, quotation.CompanyId);
                    return;
                }

                // The conversion request key derives from the quotation, so a concurrent double
                // conversion replays the same committed order instead of creating a second one.
                var orderResult = await PostSalesOrderAsync(
                    new PostSalesOrderRequest(
                        request.CompanyId,
                        quotation.CustomerId,
                        $"quotation:{quotation.Id}",
                        quotation.Lines.OrderBy(line => line.LineNumber)
                            .Select(line => new PostSalesOrderLine(line.ItemId, line.Quantity, line.UnitPrice))
                            .ToArray(),
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        $"Converted from quotation {quotation.QuotationNumber}"),
                    actor,
                    cancellationToken);

                quotation.Status = QuotationStatus.Accepted;
                quotation.AcceptedAtUtc = DateTime.UtcNow;
                quotation.SalesOrderId = orderResult.SalesOrderId;
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

                logger.LogInformation("Quotation {QuotationNumber} converted to order {OrderNumber}",
                    quotation.QuotationNumber, orderResult.OrderNumber);
                result = new QuotationConversionResult(quotation.Id, orderResult.SalesOrderId,
                    orderResult.OrderNumber, orderResult.TotalValue, Replayed: false);
            },
            cancellationToken: cancellationToken);

        if (expiredQuotation is { } expired)
        {
            throw new FinancePostingValidationException(
                $"Quotation {expired.Number} expired on {expired.ValidUntil:yyyy-MM-dd}.");
        }

        return result;
    }

    /// <inheritdoc />
    public Task<Quotation?> GetQuotationAsync(
        int companyId,
        int quotationId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.Quotations
            .Include(quotation => quotation.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(quotation => quotation.Id == quotationId && quotation.CompanyId == companyId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AgingReport> GetReceivablesAgingAsync(
        int companyId,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var asOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var invoices = await context.SalesInvoices
            .Where(invoice => invoice.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var paidByInvoice = await context.PaymentAllocations
            .Where(allocation => allocation.CompanyId == companyId)
            .GroupBy(allocation => allocation.SalesInvoiceId)
            .Select(group => new { SalesInvoiceId = group.Key, Paid = group.Sum(allocation => allocation.Amount) })
            .ToDictionaryAsync(row => row.SalesInvoiceId, row => row.Paid, cancellationToken);
        var customerNames = await context.Customers
            .Where(customer => customer.CompanyId == companyId)
            .ToDictionaryAsync(customer => customer.Id, customer => customer.Name, cancellationToken);

        var open = invoices
            .Select(invoice => new
            {
                invoice.CustomerId,
                Open = invoice.TotalValue - paidByInvoice.GetValueOrDefault(invoice.Id),
                DaysPastDue = (asOf.DayNumber - invoice.DueDate.DayNumber)
            })
            .Where(row => row.Open > 0)
            .ToList();
        var rows = open
            .GroupBy(row => row.CustomerId)
            .Select(group => new AgingBucketRow(
                group.Key,
                customerNames.GetValueOrDefault(group.Key, $"Customer {group.Key}"),
                group.Sum(row => row.DaysPastDue <= 0 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 0 and <= 30 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 30 and <= 60 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 60 and <= 90 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue > 90 ? row.Open : 0m),
                group.Sum(row => row.Open)))
            .OrderBy(row => row.TotalOpen)
            .ToList();
        return new AgingReport(companyId, asOf, "Receivables", rows, rows.Sum(row => row.TotalOpen));
    }

    private async Task<DeliveryResult> DeliverCoreAsync(
        PostDeliveryRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var order = await context.SalesOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(
                o => o.Id == request.SalesOrderId && o.CompanyId == request.CompanyId, cancellationToken)
            ?? throw new FinancePostingValidationException("Sales order not found in this company.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var orderLines = order.Lines.ToDictionary(line => line.Id);
        var priorDelivered = await context.DeliveryNoteLines
            .Where(line => order.Lines.Select(o => o.Id).Contains(line.SalesOrderLineId))
            .GroupBy(line => line.SalesOrderLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.LineId, row => row.Quantity, cancellationToken);

        var delivery = new DeliveryNote
        {
            CompanyId = request.CompanyId,
            SalesOrderId = order.Id,
            LocationId = request.LocationId,
            PostingDate = postingDate,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            DeliveredAtUtc = DateTime.UtcNow,
            DeliveredBy = actor.Trim()
        };

        var inRequestDelivered = new Dictionary<int, int>();
        foreach (var line in request.Lines)
        {
            if (!orderLines.TryGetValue(line.SalesOrderLineId, out var orderLine))
                throw new FinancePostingValidationException(
                    $"Sales-order line {line.SalesOrderLineId} does not belong to order {order.OrderNumber}.");
            if (line.Quantity <= 0)
                throw new FinancePostingValidationException("Delivery quantities must be positive.");
            priorDelivered.TryGetValue(line.SalesOrderLineId, out var prior);
            var taken = prior + inRequestDelivered.GetValueOrDefault(line.SalesOrderLineId);
            if (taken + line.Quantity > orderLine.Quantity)
                throw new FinancePostingValidationException(
                    $"Delivering {line.Quantity} more of sales-order line {line.SalesOrderLineId} would exceed " +
                    $"the ordered {orderLine.Quantity} (already delivered {taken}).");

            // The issued stock transaction is captured from the shared change tracker after the
            // stock service saves; its valuation entry is the exact COGS source of truth.
            var knownTransactionIds = context.StockTransactions.Local.Select(t => t.Id).ToHashSet();
            await stockService.SellStockAsync(
                orderLine.ItemId,
                request.LocationId,
                line.Quantity,
                notes: $"Delivery against {order.OrderNumber}",
                batchNumber: string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim(),
                expiryDate: StockLotExpiryDate.Normalize(line.ExpiryDate),
                mutationScope: mutationScope);
            var issued = context.StockTransactions.Local
                .Where(t => !knownTransactionIds.Contains(t.Id) &&
                    t.ItemId == orderLine.ItemId &&
                    t.FromLocationId == request.LocationId &&
                    t.Quantity == line.Quantity)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault()
                ?? throw new FinancePostingValidationException(
                    "The stock ledger did not record the issued movement; delivery aborted.");

            delivery.Lines.Add(new DeliveryNoteLine
            {
                CompanyId = request.CompanyId,
                LineNumber = delivery.Lines.Count + 1,
                SalesOrderLineId = orderLine.Id,
                ItemId = orderLine.ItemId,
                Quantity = line.Quantity,
                StockTransactionId = issued.Id,
                BatchNumber = string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim(),
                ExpiryDate = StockLotExpiryDate.Normalize(line.ExpiryDate)
            });
            inRequestDelivered[line.SalesOrderLineId] =
                inRequestDelivered.GetValueOrDefault(line.SalesOrderLineId) + line.Quantity;
        }

        delivery.DeliveryNumber = await documentNumbers.AllocateAsync(
            request.CompanyId, DeliveryDocumentType, postingDate.Year, "DN", cancellationToken);
        context.DeliveryNotes.Add(delivery);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        var (cogsTotal, journalNumber) = await PostCogsJournalAsync(
            delivery, company, postingDate, actor, cancellationToken);

        logger.LogInformation(
            "Delivery {DeliveryNumber} posted for company {CompanyId}: quantity {Quantity}, COGS {Cogs}",
            delivery.DeliveryNumber, request.CompanyId, delivery.Lines.Sum(line => line.Quantity), cogsTotal);

        return new DeliveryResult(
            delivery.Id,
            delivery.Lines.OrderBy(line => line.LineNumber).First().Id,
            delivery.DeliveryNumber,
            postingDate,
            delivery.Lines.Sum(line => line.Quantity),
            cogsTotal,
            journalNumber,
            Replayed: false);
    }

    private async Task<(decimal Total, string? JournalNumber)> PostCogsJournalAsync(
        DeliveryNote delivery,
        Company company,
        DateOnly postingDate,
        string actor,
        CancellationToken cancellationToken)
    {
        var cogsByLine = await context.StockValuationEntries
            .Where(entry => delivery.Lines.Select(line => line.StockTransactionId).Contains(entry.StockTransactionId))
            .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
        var cogsTotal = delivery.Lines.Sum(line => cogsByLine.GetValueOrDefault(line.StockTransactionId));
        if (cogsTotal <= 0)
        {
            return (0m, null);
        }

        var cogsAccount = await postingPolicy.ResolveAsync(
            delivery.CompanyId, AccountPostingRole.CostOfGoodsSold, cancellationToken);
        var inventoryAccount = await postingPolicy.ResolveAsync(
            delivery.CompanyId, AccountPostingRole.Inventory, cancellationToken);
        var posted = await journalPosting.PostAsync(
            new JournalPostingRequest(
                delivery.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"Cost of goods sold for delivery {delivery.DeliveryNumber}",
                DeliveryDocumentType,
                delivery.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                [
                    new JournalLineInput(cogsAccount.ChartOfAccountId, $"COGS {delivery.DeliveryNumber}", cogsTotal, 0m),
                    new JournalLineInput(inventoryAccount.ChartOfAccountId, $"Stock issued {delivery.DeliveryNumber}", 0m, cogsTotal)
                ]),
            actor,
            cancellationToken);
        return (cogsTotal, posted.JournalNumber);
    }

    private async Task<(decimal Total, string? JournalNumber)> CostOfGoodsForDeliveryAsync(
        DeliveryNote delivery,
        CancellationToken cancellationToken)
    {
        var cogsByLine = await context.StockValuationEntries
            .Where(entry => delivery.Lines.Select(line => line.StockTransactionId).Contains(entry.StockTransactionId))
            .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
        var total = delivery.Lines.Sum(line => cogsByLine.GetValueOrDefault(line.StockTransactionId));
        var journal = await context.JournalEntries.SingleOrDefaultAsync(
            entry => entry.SourceDocumentType == DeliveryDocumentType &&
                entry.SourceDocumentId == delivery.Id.ToString(CultureInfo.InvariantCulture) &&
                entry.SourceAction == "Post" &&
                entry.SourceVersion == 1,
            cancellationToken);
        return (total, journal?.JournalNumber);
    }

    private async Task<SalesInvoiceResult> InvoiceCoreAsync(
        PostSalesInvoiceRequest request,
        string requestHash,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var customer = await context.Customers.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CustomerId && candidate.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Customer not found in this company.");

        var duplicate = await context.SalesInvoices.AnyAsync(
            invoice => invoice.CompanyId == request.CompanyId && invoice.InvoiceNumber == request.InvoiceNumber,
            cancellationToken);
        if (duplicate)
            throw new FinancePostingConflictException(
                $"A sales invoice numbered '{request.InvoiceNumber}' already exists in this company.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var deliveryLineIds = request.Lines.Select(line => line.DeliveryNoteLineId).Distinct().ToList();
        var deliveryLines = await context.DeliveryNoteLines
            .Where(line => line.CompanyId == request.CompanyId && deliveryLineIds.Contains(line.Id))
            .Include(line => line.SalesOrderLine)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var deliveryLineId in deliveryLineIds)
        {
            if (!deliveryLines.ContainsKey(deliveryLineId))
                throw new FinancePostingValidationException(
                    $"Delivery line {deliveryLineId} does not exist in this company; " +
                    "invoices match posted deliveries only.");
        }

        var alreadyInvoiced = await context.SalesInvoiceLines
            .Where(line => deliveryLineIds.Contains(line.DeliveryNoteLineId))
            .Select(line => line.DeliveryNoteLineId)
            .ToListAsync(cancellationToken);
        if (alreadyInvoiced.Count > 0)
            throw new FinancePostingConflictException(
                $"Delivery lines {string.Join(", ", alreadyInvoiced.OrderBy(id => id))} " +
                "are already matched by a posted invoice; credit notes correct billed deliveries.");

        var invoice = new SalesInvoice
        {
            CompanyId = request.CompanyId,
            CustomerId = customer.Id,
            InvoiceNumber = request.InvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            PostingDate = postingDate,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim(),
            Lines = request.Lines
                .Select((line, index) =>
                {
                    var deliveryLine = deliveryLines[line.DeliveryNoteLineId];
                    // Quantities are whole units and the approved price is already at the order
                    // scale, so the product is exact — no rounding, no drift.
                    return new SalesInvoiceLine
                    {
                        CompanyId = request.CompanyId,
                        LineNumber = index + 1,
                        DeliveryNoteLineId = deliveryLine.Id,
                        ItemId = deliveryLine.ItemId,
                        Quantity = deliveryLine.Quantity,
                        RevenueValue = deliveryLine.Quantity * deliveryLine.SalesOrderLine.UnitPrice
                    };
                })
                .ToList()
        };
        invoice.TotalValue = invoice.Lines.Sum(line => line.RevenueValue);

        context.SalesInvoices.Add(invoice);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        string? journalNumber = null;
        if (invoice.TotalValue > 0)
        {
            var arAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.AccountsReceivable, cancellationToken);
            var revenueAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.SalesRevenue, cancellationToken);
            var posted = await journalPosting.PostAsync(
                new JournalPostingRequest(
                    request.CompanyId,
                    postingDate,
                    company.BaseCurrency,
                    $"Sales invoice {invoice.InvoiceNumber} to {customer.Name}",
                    InvoiceDocumentType,
                    invoice.Id.ToString(CultureInfo.InvariantCulture),
                    "Post",
                    1,
                    [
                        new JournalLineInput(arAccount.ChartOfAccountId, $"Receivable {invoice.InvoiceNumber}", invoice.TotalValue, 0m),
                        new JournalLineInput(revenueAccount.ChartOfAccountId, $"Revenue {invoice.InvoiceNumber}", 0m, invoice.TotalValue)
                    ]),
                actor,
                cancellationToken);
            journalNumber = posted.JournalNumber;
        }

        logger.LogInformation(
            "Sales invoice {InvoiceNumber} posted for company {CompanyId}: revenue {TotalValue}, journal {JournalNumber}",
            invoice.InvoiceNumber, request.CompanyId, invoice.TotalValue, journalNumber ?? "none");

        return await ToInvoiceResultAsync(invoice, replayed: false, cancellationToken);
    }

    private async Task<SalesInvoiceResult> ToInvoiceResultAsync(
        SalesInvoice invoice,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var paid = await context.PaymentAllocations
            .Where(allocation => allocation.SalesInvoiceId == invoice.Id)
            .SumAsync(allocation => (decimal?)allocation.Amount, cancellationToken) ?? 0m;
        var journal = await context.JournalEntries.SingleOrDefaultAsync(
            entry => entry.SourceDocumentType == InvoiceDocumentType &&
                entry.SourceDocumentId == invoice.Id.ToString(CultureInfo.InvariantCulture) &&
                entry.SourceAction == "Post" &&
                entry.SourceVersion == 1,
            cancellationToken);
        return new SalesInvoiceResult(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.PostingDate,
            invoice.TotalValue,
            invoice.TotalValue - paid,
            journal?.JournalNumber,
            replayed);
    }

    private Task<Company> RequireCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        context.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private static void ValidateOrderRequest(PostSalesOrderRequest request)
    {
        if (request.CompanyId <= 0 || request.CustomerId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId and CustomerId are required.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A sales-order request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A sales order requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.ItemId <= 0 || line.Quantity <= 0 || line.UnitPrice < 0)
                throw new FinancePostingValidationException(
                    "Every order line needs a positive item id and quantity, and a non-negative price.");
        }
    }

    private static void ValidateDeliveryRequest(PostDeliveryRequest request)
    {
        if (request.CompanyId <= 0 || request.SalesOrderId <= 0 || request.LocationId <= 0)
            throw new FinancePostingValidationException(
                "A positive CompanyId, SalesOrderId and LocationId are required.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A delivery request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A delivery requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.SalesOrderLineId <= 0 || line.Quantity <= 0)
                throw new FinancePostingValidationException(
                    "Every delivery line needs a positive order-line id and quantity.");
        }
    }

    private static void ValidateInvoiceRequest(PostSalesInvoiceRequest request)
    {
        if (request.CompanyId <= 0 || request.CustomerId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId and CustomerId are required.");
        var invoiceNumber = request.InvoiceNumber?.Trim() ?? string.Empty;
        if (invoiceNumber.Length is 0 or > 128)
            throw new FinancePostingValidationException("An invoice number of 1 to 128 characters is required.");
        if (request.InvoiceDate > request.DueDate)
            throw new FinancePostingValidationException("The due date must not precede the invoice date.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException("A sales-invoice request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException($"A sales invoice requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.DeliveryNoteLineId <= 0)
                throw new FinancePostingValidationException("Every invoice line needs a positive DeliveryNoteLineId.");
        }
    }

    private static string ComputeHash(Action<StringBuilder> build)
    {
        var canonical = new StringBuilder();
        build(canonical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
