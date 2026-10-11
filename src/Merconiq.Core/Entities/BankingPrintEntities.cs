namespace Merconiq.Core.Entities;

public enum BankStatementFormat
{
    Camt053 = 1,
    Mt940 = 2,
    Ofx = 3
}

public enum BankStatementFileStatus
{
    Imported = 1,
    Reconciled = 2
}

/// <summary>
/// Imported electronic bank statement file containing unallocated feed transactions.
/// </summary>
public sealed class BankStatementFile : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int BankAccountId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public BankStatementFormat Format { get; set; } = BankStatementFormat.Camt053;
    public DateOnly StatementDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public BankStatementFileStatus Status { get; set; } = BankStatementFileStatus.Imported;

    public ICollection<BankStatementTransaction> Transactions { get; set; } = new List<BankStatementTransaction>();
}

/// <summary>
/// Individual line transaction parsed from an electronic bank feed.
/// </summary>
public sealed class BankStatementTransaction : AuditableEntity
{
    public int Id { get; set; }
    public int BankStatementFileId { get; set; }
    public BankStatementFile BankStatementFile { get; set; } = null!;
    public DateOnly TransactionDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Payee { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsMatched { get; set; }
    public int? MatchedJournalEntryId { get; set; }
}

/// <summary>
/// Print format template for standard ERPNext documents with tax QR and branding.
/// </summary>
public sealed class PrintFormatTemplate : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string DocumentType { get; set; } = "SalesInvoice";
    public string Name { get; set; } = string.Empty;
    public string HeaderHtml { get; set; } = string.Empty;
    public string FooterHtml { get; set; } = string.Empty;
    public bool ShowTaxQrCode { get; set; } = true;
    public bool ShowLogo { get; set; } = true;
    public bool IsDefault { get; set; } = true;
}
