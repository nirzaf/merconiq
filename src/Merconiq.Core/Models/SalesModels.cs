using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One ordered line; quantities are base stock units, price per base unit.</summary>
public sealed record PostSalesOrderLine(int ItemId, int Quantity, decimal UnitPrice);

/// <summary>Creates one approved sales order. The order is a commitment only; nothing posts.</summary>
public sealed record PostSalesOrderRequest(
    int CompanyId,
    int CustomerId,
    string RequestKey,
    IReadOnlyList<PostSalesOrderLine> Lines,
    DateOnly? OrderDate = null,
    string? Notes = null);

/// <summary>The committed-or-replayed sales-order outcome.</summary>
public sealed record SalesOrderResult(
    int SalesOrderId,
    int FirstLineId,
    string OrderNumber,
    decimal TotalValue,
    bool Replayed);

/// <summary>One delivered line against a sales-order line.</summary>
public sealed record PostDeliveryLine(int SalesOrderLineId, int Quantity, string? BatchNumber = null, DateTime? ExpiryDate = null);

/// <summary>Posts one delivery against an approved sales order; issues valued stock and books COGS.</summary>
public sealed record PostDeliveryRequest(
    int CompanyId,
    int SalesOrderId,
    int LocationId,
    string RequestKey,
    IReadOnlyList<PostDeliveryLine> Lines,
    DateOnly? PostingDate = null,
    string? Notes = null);

/// <summary>The committed-or-replayed delivery outcome.</summary>
public sealed record DeliveryResult(
    int DeliveryNoteId,
    int FirstLineId,
    string DeliveryNumber,
    DateOnly PostingDate,
    int DeliveredQuantity,
    decimal CostOfGoodsSold,
    string? JournalNumber,
    bool Replayed);

/// <summary>One invoiced delivery line.</summary>
public sealed record PostSalesInvoiceLine(int DeliveryNoteLineId);

/// <summary>Posts one sales invoice matched to delivered lines; recognizes AR and revenue once.</summary>
public sealed record PostSalesInvoiceRequest(
    int CompanyId,
    int CustomerId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string RequestKey,
    IReadOnlyList<PostSalesInvoiceLine> Lines,
    DateOnly? PostingDate = null,
    string? Notes = null,
    bool OverrideCreditLimit = false,
    string? CreditOverrideReason = null);

/// <summary>The committed-or-replayed sales-invoice outcome.</summary>
public sealed record SalesInvoiceResult(
    int SalesInvoiceId,
    string InvoiceNumber,
    DateOnly PostingDate,
    decimal TotalValue,
    decimal OpenValue,
    string? JournalNumber,
    bool Replayed);

/// <summary>Ordered and delivered progress derived from posted deliveries.</summary>
public sealed record SalesOrderProgress(
    int SalesOrderId,
    long OrderedQuantity,
    long DeliveredQuantity,
    long OutstandingQuantity,
    IReadOnlyList<SalesOrderLineProgress> Lines);

/// <summary>Per-line delivery progress.</summary>
public sealed record SalesOrderLineProgress(
    int LineId,
    int ItemId,
    int OrderedQuantity,
    int DeliveredQuantity,
    int OutstandingQuantity);

/// <summary>One quoted line.</summary>
public sealed record PostQuotationLine(int ItemId, int Quantity, decimal UnitPrice);

/// <summary>Creates one quotation; idempotent per request key.</summary>
public sealed record PostQuotationRequest(
    int CompanyId,
    int CustomerId,
    string RequestKey,
    IReadOnlyList<PostQuotationLine> Lines,
    DateOnly? QuotationDate = null,
    DateOnly? ValidUntil = null,
    string? Notes = null);

/// <summary>The committed-or-replayed quotation outcome.</summary>
public sealed record QuotationResult(
    int QuotationId,
    string QuotationNumber,
    decimal TotalValue,
    DateOnly ValidUntil,
    bool Replayed);

/// <summary>Converts one quotation; the conversion is idempotent on the quotation itself.</summary>
public sealed record ConvertQuotationRequest(int CompanyId, int QuotationId);

/// <summary>The conversion outcome.</summary>
public sealed record QuotationConversionResult(
    int QuotationId,
    int SalesOrderId,
    string OrderNumber,
    decimal TotalValue,
    bool Replayed);
