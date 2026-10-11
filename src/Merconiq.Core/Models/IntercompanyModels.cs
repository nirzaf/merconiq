namespace Merconiq.Core.Models;

public sealed record ConfigureIntercompanyRelationshipRequest(
    int ParentCompanyId,
    int SubsidiaryCompanyId,
    int ParentReceivableAccountId,
    int ParentRevenueAccountId,
    int SubsidiaryPayableAccountId,
    int SubsidiaryExpenseAccountId);

public sealed record IntercompanyRelationshipView(
    int Id,
    int ParentCompanyId,
    int SubsidiaryCompanyId,
    int ParentReceivableAccountId,
    int ParentRevenueAccountId,
    int SubsidiaryPayableAccountId,
    int SubsidiaryExpenseAccountId,
    bool IsActive);

public sealed record PostIntercompanyTransactionRequest(
    int RelationshipId,
    int SourceCompanyId,
    int TargetCompanyId,
    string SourceDocumentType,
    int SourceDocumentId,
    string ReferenceKey,
    decimal Amount,
    string CurrencyCode,
    string? Notes);

public sealed record IntercompanyTransactionView(
    int Id,
    int RelationshipId,
    int SourceCompanyId,
    int TargetCompanyId,
    string SourceDocumentType,
    int SourceDocumentId,
    string TargetDocumentType,
    int? TargetDocumentId,
    string ReferenceKey,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string? Notes);

public sealed record IntercompanyEliminationReport(
    int ParentCompanyId,
    int SubsidiaryCompanyId,
    decimal ParentReceivableTotal,
    decimal SubsidiaryPayableTotal,
    decimal Variance,
    bool IsBalanced,
    IReadOnlyList<IntercompanyTransactionView> Transactions);
