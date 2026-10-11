using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_JournalLines_Id_CompanyId_TenantId",
                table: "JournalLines",
                columns: new[] { "Id", "CompanyId", "TenantId" });

            migrationBuilder.CreateTable(
                name: "BankStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ChartOfAccountId = table.Column<int>(type: "integer", nullable: false),
                    StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImportedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatements", x => x.Id);
                    table.UniqueConstraint("AK_BankStatements_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_BankStatements_Reference", "\"Reference\" IS NULL OR (\"Reference\" <> '' AND \"Reference\" = btrim(\"Reference\"))");
                    table.ForeignKey(
                        name: "FK_BankStatements_ChartOfAccounts_ChartOfAccountId_CompanyId_T~",
                        columns: x => new { x.ChartOfAccountId, x.CompanyId, x.TenantId },
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatements_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankStatementId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.UniqueConstraint("AK_BankStatementLines_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_BankStatementLines_Amount", "\"Amount\" <> 0");
                    table.CheckConstraint("CK_BankStatementLines_Description", "\"Description\" <> '' AND \"Description\" = btrim(\"Description\")");
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankStatements_BankStatementId_CompanyId~",
                        columns: x => new { x.BankStatementId, x.CompanyId, x.TenantId },
                        principalTable: "BankStatements",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankStatementLineId = table.Column<int>(type: "integer", nullable: false),
                    JournalLineId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnmatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UnmatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UnmatchReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementMatches", x => x.Id);
                    table.CheckConstraint("CK_BankStatementMatches_Withdrawal", "\"IsActive\" OR (\"UnmatchedAtUtc\" IS NOT NULL AND \"UnmatchReason\" IS NOT NULL AND \"UnmatchReason\" <> '')");
                    table.ForeignKey(
                        name: "FK_BankStatementMatches_BankStatementLines_BankStatementLineId~",
                        columns: x => new { x.BankStatementLineId, x.CompanyId, x.TenantId },
                        principalTable: "BankStatementLines",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementMatches_JournalLines_JournalLineId_CompanyId_T~",
                        columns: x => new { x.JournalLineId, x.CompanyId, x.TenantId },
                        principalTable: "JournalLines",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_BankStatementId_CompanyId_TenantId",
                table: "BankStatementLines",
                columns: new[] { "BankStatementId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_TenantId_BankStatementId_LineNumber",
                table: "BankStatementLines",
                columns: new[] { "TenantId", "BankStatementId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementMatches_BankStatementLineId_CompanyId_TenantId",
                table: "BankStatementMatches",
                columns: new[] { "BankStatementLineId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementMatches_JournalLineId_CompanyId_TenantId",
                table: "BankStatementMatches",
                columns: new[] { "JournalLineId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementMatches_ActiveJournalLine",
                table: "BankStatementMatches",
                columns: new[] { "TenantId", "CompanyId", "JournalLineId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementMatches_ActiveLine",
                table: "BankStatementMatches",
                columns: new[] { "TenantId", "CompanyId", "BankStatementLineId" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_ChartOfAccountId_CompanyId_TenantId",
                table: "BankStatements",
                columns: new[] { "ChartOfAccountId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatements_CompanyId_TenantId",
                table: "BankStatements",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_BankStatements_Account_Date",
                table: "BankStatements",
                columns: new[] { "TenantId", "CompanyId", "ChartOfAccountId", "StatementDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementMatches");

            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "BankStatements");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_JournalLines_Id_CompanyId_TenantId",
                table: "JournalLines");
        }
    }
}
