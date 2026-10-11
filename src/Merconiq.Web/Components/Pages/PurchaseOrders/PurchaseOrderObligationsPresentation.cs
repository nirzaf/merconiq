using Merconiq.Core.Models;

namespace Merconiq.Web.Components.Pages.PurchaseOrders;

internal static class PurchaseOrderObligationsPresentation
{
    internal const string ReceiptScopeNotice =
        "Received and rejected quantities derive from posted goods receipts; invoiced balances remain future scope.";

    internal static string GetOrderedSummary(PurchaseOrderLineObligations obligations) =>
        $"{obligations.OrderedQuantity} ordered units across {obligations.Lines.Count} lines; " +
        $"{obligations.ReceivedQuantity} received, {obligations.OutstandingQuantity} outstanding.";
}
