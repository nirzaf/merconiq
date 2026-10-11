namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one fixed asset: registered, capitalized into the ledger, then disposed of.</summary>
public enum FixedAssetStatus
{
    Registered = 0,
    Capitalized = 1,
    Disposed = 2
}
