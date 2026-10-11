using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>The shift state after a till-approval decision.</summary>
public sealed record TillApprovalResult(
    int PosShiftId,
    PosShiftStatus Status,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal OverShortValue,
    string? OverShortJournalNumber,
    string? ClosedBy,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    string? RejectedBy,
    string? RejectionReason);
