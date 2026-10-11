using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

public sealed record MaterialRequestLineInput(
    int ItemId,
    int Quantity);

public sealed record CreateMaterialRequest(
    int CompanyId,
    DateOnly RequestedDate,
    DateOnly? RequiredDate,
    MaterialRequestPurpose Purpose,
    string RequestedBy,
    IReadOnlyList<MaterialRequestLineInput> Lines,
    string? Notes = null);

public sealed record MaterialRequestResult(
    int MaterialRequestId,
    string RequestNumber,
    MaterialRequestStatus Status,
    int TotalItemCount);

public sealed record SupplierQuotationLineInput(
    int ItemId,
    int Quantity,
    decimal Rate);

public sealed record CreateSupplierQuotationRequest(
    int CompanyId,
    int SupplierId,
    DateOnly QuotationDate,
    DateOnly? ValidUntil,
    string Currency,
    IReadOnlyList<SupplierQuotationLineInput> Lines,
    string? Notes = null);

public sealed record SupplierQuotationComparisonItem(
    int SupplierId,
    string SupplierName,
    int SupplierQuotationId,
    string QuotationNumber,
    DateOnly QuotationDate,
    decimal Rate,
    decimal TotalAmount);

public sealed record SupplierQuotationComparisonReport(
    int ItemId,
    string ItemCode,
    IReadOnlyList<SupplierQuotationComparisonItem> Quotes);
