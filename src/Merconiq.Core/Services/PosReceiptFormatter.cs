using System.Globalization;
using System.Text;
using Merconiq.Core.Models;

namespace Merconiq.Core.Services;

/// <summary>
/// Renders a committed sale as plain monospace text for screen display and thermal printers. Every
/// line fits the requested width, money uses the company's currency scale, and control characters
/// from master data are replaced so a product name cannot inject extra lines into a printout.
/// </summary>
public static class PosReceiptFormatter
{
    /// <summary>Column width of a standard 80 mm thermal roll in a common monospace font.</summary>
    public const int DefaultWidth = 40;

    /// <summary>The narrowest layout that still keeps labels and amounts on one line.</summary>
    public const int MinimumWidth = 24;

    private const int MaximumScale = 6;

    /// <summary>Formats the receipt as text terminated by a single newline.</summary>
    public static string Format(PosReceipt receipt, int width = DefaultWidth)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (width < MinimumWidth)
            throw new ArgumentOutOfRangeException(nameof(width), $"A receipt needs at least {MinimumWidth} columns.");

        var scale = Math.Clamp(receipt.CurrencyScale, 0, MaximumScale);
        var output = new StringBuilder();
        void Line(string text) => output.Append(text).Append('\n');

        Line(Center(Clean(receipt.CompanyName), width));
        if (!string.IsNullOrWhiteSpace(receipt.TradingName))
            Line(Center(Clean(receipt.TradingName), width));
        Line(Rule(width));
        Line(Pair("Receipt", $"POS-{receipt.SaleId}", width));
        Line(Pair("Date", receipt.SoldAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC", width));
        Line(Pair("Register", Clean(receipt.RegisterName), width));
        Line(Pair("Shift", receipt.ShiftId.ToString(CultureInfo.InvariantCulture), width));
        Line(Pair("Cashier", Clean(receipt.SoldBy), width));
        Line(Rule(width));
        foreach (var line in receipt.Lines)
        {
            Line(Fit($"{Clean(line.ItemCode)} {Clean(line.ItemDescription)}", width));
            Line(Pair(
                $"  {line.Quantity.ToString(CultureInfo.InvariantCulture)} x {Money(line.UnitPrice, scale)}",
                Money(line.LineValue, scale),
                width));
        }
        Line(Rule(width));
        Line(Pair("TOTAL", Money(receipt.Total, scale), width));
        foreach (var tender in receipt.Tenders)
            Line(Pair(Clean(tender.MethodName), Money(tender.Amount, scale), width));
        Line(Rule(width));
        Line(Center("Thank you for your purchase", width));
        return output.ToString();
    }

    private static string Money(decimal amount, int scale) =>
        amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Rule(int width) => new('-', width);

    private static string Center(string text, int width)
    {
        var fitted = Fit(text, width);
        var padding = (width - fitted.Length) / 2;
        return new string(' ', padding) + fitted;
    }

    /// <summary>Places a label at the left edge and a value at the right edge, truncating the label if needed.</summary>
    private static string Pair(string label, string value, int width)
    {
        var right = Fit(value, width);
        var leftWidth = width - right.Length - 1;
        var left = Fit(label, Math.Max(leftWidth, 0));
        return left + new string(' ', width - left.Length - right.Length) + right;
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..width];

    /// <summary>Replaces control characters with spaces and trims, so master data cannot break the layout.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var characters = value.Select(character => char.IsControl(character) ? ' ' : character).ToArray();
        return new string(characters).Trim();
    }
}
