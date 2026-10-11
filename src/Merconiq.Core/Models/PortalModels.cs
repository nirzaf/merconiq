namespace Merconiq.Core.Models;

public sealed record CustomerPortalOverview(
    int CustomerId,
    string CustomerName,
    decimal TotalOutstandingBalance,
    IReadOnlyList<CustomerOrderSummary> Orders,
    IReadOnlyList<CustomerInvoiceSummary> Invoices,
    IReadOnlyList<CustomerDeliverySummary> Deliveries);

public sealed record CustomerOrderSummary(
    int OrderId,
    string OrderNumber,
    DateOnly OrderDate,
    string Status,
    decimal TotalAmount);

public sealed record CustomerInvoiceSummary(
    int InvoiceId,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string Status,
    decimal TotalAmount,
    decimal OutstandingBalance);

public sealed record CustomerDeliverySummary(
    int DeliveryId,
    string DeliveryNumber,
    DateOnly DeliveryDate,
    string Status);

public sealed record SupplierPortalOverview(
    int SupplierId,
    string SupplierName,
    IReadOnlyList<SupplierOrderSummary> Orders,
    IReadOnlyList<SupplierCollaborationSummary> RecentSubmissions);

public sealed record SupplierOrderSummary(
    int OrderId,
    string OrderNumber,
    DateTime OrderDate,
    string Status,
    decimal TotalAmount);

public sealed record SupplierCollaborationSummary(
    int Id,
    int PurchaseOrderId,
    string EntryType,
    string? SupplierReference,
    DateOnly? ProposedDeliveryDate,
    string? InvoiceReferenceNumber,
    decimal? InvoiceAmount,
    string Status,
    string? BuyerReviewNotes);

public sealed record SubmitSupplierAcknowledgmentRequest(
    int SupplierId,
    int PurchaseOrderId,
    string SupplierReference,
    DateOnly ProposedDeliveryDate);

public sealed record SubmitSupplierInvoiceReferenceRequest(
    int SupplierId,
    int PurchaseOrderId,
    string InvoiceReferenceNumber,
    decimal InvoiceAmount);

public sealed record ReviewSupplierSubmissionRequest(
    int SubmissionId,
    bool Approve,
    string? ReviewNotes);
