using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Merconiq.Core.Services;

/// <summary>
/// Outbound webhook signature scheme (HMAC-SHA256 over "timestamp.payload") so receivers can
/// verify authenticity AND freshness — a bare payload hash alone would let a captured payload
/// be replayed indefinitely. Receivers reject signatures older than their tolerance.
/// </summary>
public static class WebhookSignature
{
    /// <summary>Header carrying the unix-seconds timestamp included in the signature.</summary>
    public const string TimestampHeader = "X-Inventory-Timestamp";

    /// <summary>Header carrying the lowercase hex HMAC-SHA256 of "timestamp.payload".</summary>
    public const string SignatureHeader = "X-Inventory-Signature";

    /// <summary>Computes the lowercase hex signature for the supplied unix-seconds timestamp and payload.</summary>
    public static string Compute(string secret, long timestampUnixSeconds, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var signed = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{timestampUnixSeconds}.{payload}"));
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(signed)).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a signature against the payload with a freshness tolerance; comparison is
    /// constant-time so response timing cannot leak the expected value.
    /// </summary>
    public static bool Verify(
        string secret, long timestampUnixSeconds, string payload, string signature,
        long nowUnixSeconds, long toleranceSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(signature) ||
            Math.Abs(nowUnixSeconds - timestampUnixSeconds) > toleranceSeconds)
        {
            return false;
        }

        var expected = Compute(secret, timestampUnixSeconds, payload);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
