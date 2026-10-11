namespace Merconiq.Core.Models;

/// <summary>Ordered charge-line quantity recorded on the purchase order.</summary>
public sealed record PurchaseOrderLineObligation(
    int LineId,
    int ItemId,
    string? ItemCode,
    string? ItemDescription,
    int OrderedQuantity,
    int ReceivedQuantity = 0,
    int RejectedQuantity = 0);

/// <summary>
/// Read-only ordered-line obligations. Received and rejected quantities derive from posted
/// goods-receipt source lines, never from mutable counters; outstanding is the quantity still
/// expected to arrive: ordered minus received minus rejected.
/// </summary>
public sealed record PurchaseOrderLineObligations(
    int PurchaseOrderId,
    long OrderedQuantity,
    long ReceivedQuantity,
    long OutstandingQuantity,
    IReadOnlyList<PurchaseOrderLineObligation> Lines);
