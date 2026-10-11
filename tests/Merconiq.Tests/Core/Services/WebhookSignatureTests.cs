using FluentAssertions;
using Merconiq.Core.Services;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Merconiq.Tests.Core.Services;

/// <summary>Pure tests for the replay-protected webhook signature scheme.</summary>
public class WebhookSignatureTests
{
    private const string Secret = "whsec_test_secret_0123456789abcdef";
    private const long Timestamp = 1_760_000_000L;
    private const string Payload = """{"eventType":"Stock.Received","quantity":5}""";

    [Fact]
    public void Compute_MatchesIndependentHmacOfTimestampDotPayload()
    {
        // Independently computed with the raw primitive, mirroring how a receiver verifies.
        var expected = Convert.ToHexString(
            new HMACSHA256(Encoding.UTF8.GetBytes(Secret))
                .ComputeHash(Encoding.UTF8.GetBytes($"{Timestamp}.{Payload}"))).ToLowerInvariant();

        WebhookSignature.Compute(Secret, Timestamp, Payload).Should().Be(expected);
    }

    [Fact]
    public void Verify_AcceptsFreshCorrectSignature_AndRejectsTampering()
    {
        var signature = WebhookSignature.Compute(Secret, Timestamp, Payload);
        WebhookSignature.Verify(Secret, Timestamp, Payload, signature, Timestamp).Should().BeTrue();
        WebhookSignature.Verify(Secret, Timestamp, Payload + " ", signature, Timestamp).Should().BeFalse();
        WebhookSignature.Verify(Secret, Timestamp, Payload, signature[..63] + "0", Timestamp).Should().BeFalse();
        WebhookSignature.Verify("other-secret", Timestamp, Payload, signature, Timestamp).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsStaleTimestamps_BeyondTolerance()
    {
        var signature = WebhookSignature.Compute(Secret, Timestamp, Payload);
        WebhookSignature.Verify(Secret, Timestamp, Payload, signature, Timestamp + 299).Should().BeTrue();
        WebhookSignature.Verify(Secret, Timestamp, Payload, signature, Timestamp + 301).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsBlankSignatures()
    {
        WebhookSignature.Verify(Secret, Timestamp, Payload, "", Timestamp).Should().BeFalse();
        WebhookSignature.Verify(Secret, Timestamp, Payload, "  ", Timestamp).Should().BeFalse();
    }
}
