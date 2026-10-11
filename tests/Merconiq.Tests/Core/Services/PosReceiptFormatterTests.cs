using FluentAssertions;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Pure receipt layout rules: every line fits the printer width, amounts follow the company's currency
/// scale, master-data text cannot inject lines, and the receipt never reads outside its own fields.
/// </summary>
public class PosReceiptFormatterTests
{
    [Fact]
    public void Format_KeepsEveryLineWithinTheRequestedWidth()
    {
        var receipt = Receipt(
            lines:
            [
                new PosReceiptLine("SKU-1", new string('x', 200), 3, 12.5m, 37.5m)
            ]);

        var text = PosReceiptFormatter.Format(receipt);

        var lines = text.TrimEnd('\n').Split('\n');
        lines.Should().OnlyContain(line => line.Length <= PosReceiptFormatter.DefaultWidth);
        text.Should().EndWith("\n");
    }

    [Fact]
    public void Format_RendersTotalsAndTendersWithTheCompanyScale()
    {
        var receipt = Receipt(
            scale: 2,
            total: 30m,
            lines: [new PosReceiptLine("SKU-1", "Coffee", 2, 15m, 30m)],
            tenders: [new PosReceiptTender("Cash", 20m), new PosReceiptTender("Bank transfer", 10m)]);

        var text = PosReceiptFormatter.Format(receipt);

        text.Should().Contain("SKU-1 Coffee");
        text.Should().Contain("  2 x 15.00");
        text.Should().Contain("TOTAL");
        text.Should().Contain("Cash");
        text.Should().Contain("Bank transfer");
        AmountOnLine(text, "TOTAL").Should().Be("30.00");
        AmountOnLine(text, "Cash").Should().Be("20.00");
        AmountOnLine(text, "Bank transfer").Should().Be("10.00");
    }

    [Fact]
    public void Format_ZeroDecimalCurrencyPrintsWholeAmounts()
    {
        var receipt = Receipt(scale: 0, total: 30m, lines: [new PosReceiptLine("SKU-1", "Rice", 1, 30m, 30m)]);

        AmountOnLine(PosReceiptFormatter.Format(receipt), "TOTAL").Should().Be("30");
    }

    [Fact]
    public void Format_ReplacesControlCharactersSoMasterDataCannotAddLines()
    {
        var receipt = Receipt(
            lines: [new PosReceiptLine("SKU-9", "Mug\r\nTOTAL 0.01\tfake", 1, 5m, 5m)]);

        var text = PosReceiptFormatter.Format(receipt);

        text.TrimEnd('\n').Split('\n').Should().NotContain(line => line.StartsWith("TOTAL 0.01"));
        text.Should().NotContain("\r").And.NotContain("\t");
        text.Should().Contain("SKU-9 Mug  TOTAL 0.01 fake", "CR and LF become spaces inside the item line");
    }

    [Fact]
    public void Format_CentersTheCompanyNameAndSkipsAnEmptyTradingName()
    {
        var receipt = Receipt(companyName: "ACME", tradingName: "  ");

        var firstLine = PosReceiptFormatter.Format(receipt).Split('\n')[0];

        firstLine.Trim().Should().Be("ACME");
        firstLine.Should().StartWith(" ", "a short company name is centered");
    }

    [Fact]
    public void Format_ShowsTheTradingNameUnderTheLegalName()
    {
        var receipt = Receipt(companyName: "ACME Holdings Ltd", tradingName: "ACME Shop");

        var lines = PosReceiptFormatter.Format(receipt).Split('\n');

        lines[0].Trim().Should().Be("ACME Holdings Ltd");
        lines[1].Trim().Should().Be("ACME Shop");
    }

    [Fact]
    public void Format_RejectsWidthsTooNarrowForTheLayout()
    {
        var act = () => PosReceiptFormatter.Format(Receipt(), width: PosReceiptFormatter.MinimumWidth - 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static string AmountOnLine(string text, string label)
    {
        // The space keeps "Cash" from also matching the "Cashier" line.
        var line = text.Split('\n').Single(candidate => candidate.StartsWith(label + " ", StringComparison.Ordinal));
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
    }

    private static PosReceipt Receipt(
        string companyName = "ACME",
        string? tradingName = null,
        int scale = 2,
        decimal total = 30m,
        IReadOnlyList<PosReceiptLine>? lines = null,
        IReadOnlyList<PosReceiptTender>? tenders = null) => new(
        CompanyId: 1,
        SaleId: 42,
        CompanyName: companyName,
        TradingName: tradingName,
        RegisterName: "Front till",
        ShiftId: 7,
        SoldAtUtc: new DateTime(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc),
        SoldBy: "cashier-1",
        CurrencyScale: scale,
        Total: total,
        Lines: lines ?? [new PosReceiptLine("SKU-1", "Coffee", 2, 15m, 30m)],
        Tenders: tenders ?? [new PosReceiptTender("Cash", total)]);
}
