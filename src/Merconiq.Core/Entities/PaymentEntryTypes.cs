namespace Merconiq.Core.Entities;

/// <summary>Direction of a posted payment entry.</summary>
public enum PaymentEntryType
{
    /// <summary>Money paid out to a supplier against payables.</summary>
    Pay = 0,

    /// <summary>Money received from a customer against receivables.</summary>
    Receive = 1,

    /// <summary>Money moved between two of the company's own accounts; no party involved.</summary>
    InternalTransfer = 2
}

/// <summary>Tender recorded for a payment; the posting policy maps it to a control account.</summary>
public enum PaymentMethod
{
    BankTransfer = 0,
    Cash = 1,

    /// <summary>
    /// Card tender. Posts to the bank control account like a bank transfer; it is matched to the
    /// acquirer's settlement batches by the card settlement reconciliation.
    /// </summary>
    Card = 2
}
