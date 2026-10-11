using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One invoice line matched to a posted goods-receipt line; the receipt line is consumed.</summary>
public sealed record PostPurchaseInvoiceLine(int GoodsReceiptLineId);

/// <summary>
/// Posts one supplier invoice matched to posted receipts. The request key makes the invoice
/// idempotent: committed requests replay, conflicting payloads are rejected.
/// </summary>
public sealed record PostPurchaseInvoiceRequest(
    int CompanyId,
    int SupplierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string RequestKey,
    IReadOnlyList<PostPurchaseInvoiceLine> Lines,
    DateOnly? PostingDate = null,
    string? Notes = null);

/// <summary>The committed-or-replayed invoice outcome.</summary>
public sealed record PurchaseInvoiceResult(
    int PurchaseInvoiceId,
    string InvoiceNumber,
    DateOnly PostingDate,
    decimal TotalValue,
    decimal OpenValue,
    string? JournalNumber,
    bool Replayed);

/// <summary>One requested allocation of payment money against an open purchase invoice.</summary>
public sealed record PostPaymentAllocation(int PurchaseInvoiceId, decimal Amount);

/// <summary>
/// Posts one payment. The request key makes the payment idempotent; the amount must be fully
/// allocated in the same commit, and over-allocation against open invoices is rejected.
/// </summary>
public sealed record PostPaymentRequest(
    int CompanyId,
    PaymentEntryType PaymentType,
    PaymentMethod Method,
    string RequestKey,
    decimal Amount,
    IReadOnlyList<PostPaymentAllocation> Allocations,
    int? SupplierId = null,
    int? CustomerId = null,
    DateOnly? PostingDate = null,
    string? Reference = null,
    string? Notes = null,
    /// <summary>Unallocated money paid to (or received from) the party; reduces their open balance.</summary>
    bool IsAdvance = false,
    /// <summary>Internal transfers only: the account money leaves.</summary>
    int? FromAccountId = null,
    /// <summary>Internal transfers only: the account money arrives in.</summary>
    int? ToAccountId = null);

/// <summary>The committed-or-replayed payment outcome.</summary>
public sealed record PaymentResult(
    int PaymentEntryId,
    DateOnly PostingDate,
    decimal Amount,
    decimal AllocatedValue,
    string? JournalNumber,
    bool Replayed);

/// <summary>One open payable document with its derived outstanding balance.</summary>
public sealed record PayableOpenItem(
    int PurchaseInvoiceId,
    int SupplierId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal TotalValue,
    decimal PaidValue,
    decimal OpenValue);

/// <summary>Company payables aging derived from posted invoices and allocations.</summary>
public sealed record PayablesOpenSummary(
    int CompanyId,
    DateOnly? AsOfDate,
    IReadOnlyList<PayableOpenItem> Items,
    decimal TotalOpenValue);
