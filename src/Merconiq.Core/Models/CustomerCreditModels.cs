namespace Merconiq.Core.Models;

/// <summary>A customer's credit limit, open receivable in base currency, and the headroom left under the limit.</summary>
public sealed record CustomerCreditExposure(
    int CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal? CreditLimit,
    decimal OpenReceivable,
    decimal? Available);
