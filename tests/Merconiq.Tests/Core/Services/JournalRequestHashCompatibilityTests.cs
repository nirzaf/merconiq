using FluentAssertions;
using Merconiq.Core.Models;
using Merconiq.Core.Services;

namespace Merconiq.Tests.Core.Services;

/// <summary>
/// Committed journals store their request hash, and retries are judged against it. Adding optional
/// adjustment-period targeting must leave every regular request's hash byte-identical, and a targeted
/// request must hash differently from the same payload posted by date.
/// </summary>
public class JournalRequestHashCompatibilityTests
{
    /// <summary>
    /// Reference digest of this regular request, computed independently from the field layout the kernel
    /// has always used (unit separator after every field). If this changes, previously committed journals
    /// would start failing their idempotent replay checks.
    /// </summary>
    private const string RegularRequestReferenceHash =
        "2c1b75909e4b06430f6307477e1ce0bf7cbdc63fe8fae929de8567b070381424";

    [Fact]
    public void RegularRequest_HashMatchesTheHistoricalFieldLayout()
    {
        var request = RegularRequest(fiscalPeriodId: null);

        JournalPostingRules.ComputeRequestHash(request).Should().Be(RegularRequestReferenceHash);
    }

    [Fact]
    public void TargetedRequest_HashesDifferentlyFromTheSamePayloadPostedByDate()
    {
        var regular = RegularRequest(fiscalPeriodId: null);
        var targeted = RegularRequest(fiscalPeriodId: 77);

        JournalPostingRules.ComputeRequestHash(targeted)
            .Should().NotBe(JournalPostingRules.ComputeRequestHash(regular));
    }

    [Fact]
    public void TargetedRequest_DifferentTargetsHashDifferently()
    {
        JournalPostingRules.ComputeRequestHash(RegularRequest(fiscalPeriodId: 77))
            .Should().NotBe(JournalPostingRules.ComputeRequestHash(RegularRequest(fiscalPeriodId: 78)));
    }

    private static JournalPostingRequest RegularRequest(int? fiscalPeriodId) => new(
        1,
        new DateOnly(2026, 1, 15),
        "USD",
        "Opening balance",
        "OpeningBalance",
        "doc-1",
        "Post",
        1,
        [
            new JournalLineInput(1000, "Bank", 100m, 0m),
            new JournalLineInput(3000, "Equity", 0m, 100m)
        ],
        fiscalPeriodId);
}
