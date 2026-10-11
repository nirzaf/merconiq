using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFxIntercompanyPortals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FxRevaluationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    RevaluationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalUnrealizedGain = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TotalUnrealizedLoss = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    ReversalJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRevaluationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntercompanyRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentCompanyId = table.Column<int>(type: "integer", nullable: false),
                    SubsidiaryCompanyId = table.Column<int>(type: "integer", nullable: false),
                    ParentReceivableAccountId = table.Column<int>(type: "integer", nullable: false),
                    ParentRevenueAccountId = table.Column<int>(type: "integer", nullable: false),
                    SubsidiaryPayableAccountId = table.Column<int>(type: "integer", nullable: false),
                    SubsidiaryExpenseAccountId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntercompanyRelationships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntercompanyTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelationshipId = table.Column<int>(type: "integer", nullable: false),
                    SourceCompanyId = table.Column<int>(type: "integer", nullable: false),
                    TargetCompanyId = table.Column<int>(type: "integer", nullable: false),
                    SourceDocumentType = table.Column<string>(type: "text", nullable: false),
                    SourceDocumentId = table.Column<int>(type: "integer", nullable: false),
                    TargetDocumentType = table.Column<string>(type: "text", nullable: false),
                    TargetDocumentId = table.Column<int>(type: "integer", nullable: true),
                    ReferenceKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntercompanyTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortalContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PortalType = table.Column<int>(type: "integer", nullable: false),
                    PartyId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    InviteToken = table.Column<string>(type: "text", nullable: true),
                    InviteExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalContacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCollaborationEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: false),
                    EntryType = table.Column<int>(type: "integer", nullable: false),
                    SupplierReference = table.Column<string>(type: "text", nullable: true),
                    ProposedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceReferenceNumber = table.Column<string>(type: "text", nullable: true),
                    InvoiceAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BuyerReviewNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCollaborationEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_TenantId_CompanyId_FromCurrency_ToCurrency_Ef~",
                table: "ExchangeRates",
                columns: new[] { "TenantId", "CompanyId", "FromCurrency", "ToCurrency", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FxRevaluationRuns_TenantId_CompanyId_FiscalPeriodId_Status",
                table: "FxRevaluationRuns",
                columns: new[] { "TenantId", "CompanyId", "FiscalPeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyRelationships_TenantId_ParentCompanyId_Subsidia~",
                table: "IntercompanyRelationships",
                columns: new[] { "TenantId", "ParentCompanyId", "SubsidiaryCompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntercompanyTransactions_TenantId_ReferenceKey",
                table: "IntercompanyTransactions",
                columns: new[] { "TenantId", "ReferenceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalContacts_TenantId_CompanyId_PortalType_Email",
                table: "PortalContacts",
                columns: new[] { "TenantId", "CompanyId", "PortalType", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCollaborationEntries_TenantId_CompanyId_SupplierId_~",
                table: "SupplierCollaborationEntries",
                columns: new[] { "TenantId", "CompanyId", "SupplierId", "PurchaseOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "FxRevaluationRuns");

            migrationBuilder.DropTable(
                name: "IntercompanyRelationships");

            migrationBuilder.DropTable(
                name: "IntercompanyTransactions");

            migrationBuilder.DropTable(
                name: "PortalContacts");

            migrationBuilder.DropTable(
                name: "SupplierCollaborationEntries");
        }
    }
}
