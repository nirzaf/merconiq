using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancePostingKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyAccountMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChartOfAccountId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyAccountMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyAccountMappings_ChartOfAccounts_ChartOfAccountId_Com~",
                        columns: x => new { x.ChartOfAccountId, x.CompanyId, x.TenantId },
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountMappings_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                    table.UniqueConstraint("AK_FiscalYears_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_FiscalYears_Range", "\"EndDate\" >= \"StartDate\" AND \"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                    table.ForeignKey(
                        name: "FK_FiscalYears_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    JournalNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceDocumentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceAction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceVersion = table.Column<int>(type: "integer", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalDebit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TotalCredit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ReversalOfJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.UniqueConstraint("AK_JournalEntries_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.CheckConstraint("CK_JournalEntries_Balanced", "\"TotalDebit\" >= 0 AND \"TotalCredit\" >= 0 AND \"TotalDebit\" = \"TotalCredit\" AND \"TotalDebit\" > 0");
                    table.ForeignKey(
                        name: "FK_JournalEntries_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntries_JournalEntries_ReversalOfJournalEntryId_Tena~",
                        columns: x => new { x.ReversalOfJournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FiscalYearId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReopenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReopenReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                    table.CheckConstraint("CK_FiscalPeriods_Range", "\"EndDate\" >= \"StartDate\" AND \"PeriodNumber\" BETWEEN 1 AND 13 AND \"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                    table.ForeignKey(
                        name: "FK_FiscalPeriods_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalPeriods_FiscalYears_FiscalYearId_CompanyId_TenantId",
                        columns: x => new { x.FiscalYearId, x.CompanyId, x.TenantId },
                        principalTable: "FiscalYears",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ChartOfAccountId = table.Column<int>(type: "integer", nullable: false),
                    AccountCodeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Debit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.CheckConstraint("CK_JournalLines_OneSide", "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0))");
                    table.ForeignKey(
                        name: "FK_JournalLines_ChartOfAccounts_ChartOfAccountId_CompanyId_Ten~",
                        columns: x => new { x.ChartOfAccountId, x.CompanyId, x.TenantId },
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_JournalEntryId_TenantId",
                        columns: x => new { x.JournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountMappings_ChartOfAccountId_CompanyId_TenantId",
                table: "CompanyAccountMappings",
                columns: new[] { "ChartOfAccountId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountMappings_CompanyId_TenantId",
                table: "CompanyAccountMappings",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountMappings_TenantId_CompanyId_Role",
                table: "CompanyAccountMappings",
                columns: new[] { "TenantId", "CompanyId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_CompanyId_TenantId",
                table: "FiscalPeriods",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_FiscalYearId_CompanyId_TenantId",
                table: "FiscalPeriods",
                columns: new[] { "FiscalYearId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_TenantId_CompanyId_StartDate",
                table: "FiscalPeriods",
                columns: new[] { "TenantId", "CompanyId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_TenantId_FiscalYearId_PeriodNumber",
                table: "FiscalPeriods",
                columns: new[] { "TenantId", "FiscalYearId", "PeriodNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_CompanyId_TenantId",
                table: "FiscalYears",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_TenantId_CompanyId",
                table: "FiscalYears",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId_TenantId",
                table: "JournalEntries",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversalOfJournalEntryId_TenantId",
                table: "JournalEntries",
                columns: new[] { "ReversalOfJournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_CompanyId_JournalNumber",
                table: "JournalEntries",
                columns: new[] { "TenantId", "CompanyId", "JournalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_CompanyId_PostingDate",
                table: "JournalEntries",
                columns: new[] { "TenantId", "CompanyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntries_SourceAction",
                table: "JournalEntries",
                columns: new[] { "TenantId", "CompanyId", "SourceDocumentType", "SourceDocumentId", "SourceAction", "SourceVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_ChartOfAccountId_CompanyId_TenantId",
                table: "JournalLines",
                columns: new[] { "ChartOfAccountId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_CompanyId_TenantId",
                table: "JournalLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_JournalEntryId_TenantId",
                table: "JournalLines",
                columns: new[] { "JournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_TenantId_CompanyId_ChartOfAccountId",
                table: "JournalLines",
                columns: new[] { "TenantId", "CompanyId", "ChartOfAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_TenantId_JournalEntryId_LineNumber",
                table: "JournalLines",
                columns: new[] { "TenantId", "JournalEntryId", "LineNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyAccountMappings");

            migrationBuilder.DropTable(
                name: "FiscalPeriods");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "FiscalYears");

            migrationBuilder.DropTable(
                name: "JournalEntries");
        }
    }
}
