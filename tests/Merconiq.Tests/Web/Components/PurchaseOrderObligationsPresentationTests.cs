using FluentAssertions;
using Merconiq.Core.Models;
using Merconiq.Web.Components.Pages.PurchaseOrders;

namespace Merconiq.Tests.Web.Components;

public sealed class PurchaseOrderObligationsPresentationTests
{
    [Fact]
    public void SummaryShowsOrderedReceivedAndOutstandingQuantitiesFromPostedReceipts()
    {
        var obligations = new PurchaseOrderLineObligations(
            PurchaseOrderId: 42,
            OrderedQuantity: 12,
            ReceivedQuantity: 5,
            OutstandingQuantity: 7,
            Lines:
            [
                new PurchaseOrderLineObligation(1, 2, "ITEM-1", "First item", 5, ReceivedQuantity: 5),
                new PurchaseOrderLineObligation(2, 3, "ITEM-2", "Second item", 7)
            ]);

        PurchaseOrderObligationsPresentation.GetOrderedSummary(obligations)
            .Should().Be("12 ordered units across 2 lines; 5 received, 7 outstanding.");
        PurchaseOrderObligationsPresentation.ReceiptScopeNotice
            .Should().Contain("posted goods receipts")
            .And.NotContain("not projected");
    }
}
