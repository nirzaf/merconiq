namespace Merconiq.Core.Entities;

/// <summary>
/// Named control-account roles that posting adapters resolve per company. Role names are the
/// stable contract; the mapped chart account is always company-supplied configuration, never a
/// hard-coded identifier.
/// </summary>
public enum AccountPostingRole
{
    Inventory = 0,
    GoodsReceivedNotInvoiced = 1,
    AccountsPayable = 2,
    AccountsReceivable = 3,
    Bank = 4,
    CashOnHand = 5,
    CostOfGoodsSold = 6,
    SalesRevenue = 7,
    TaxPayable = 8,
    Rounding = 9,
    OpeningEquity = 10,
    StockInTransit = 11,
    CashOverShort = 12,
    FixedAssets = 13,
    AccumulatedDepreciation = 14,
    DepreciationExpense = 15,
    GainOnDisposal = 16,
    LossOnDisposal = 17,
    EmployeeExpensesPayable = 18,
    SalaryExpense = 19,
    DeductionsPayable = 20
}
