using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ClaimNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClaimantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClaimDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApprovalJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaymentJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseClaims", x => x.Id);
                    table.UniqueConstraint("AK_ExpenseClaims_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_ExpenseClaims_Lifecycle", "(\"Status\" = 'Draft' AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR (\"Status\" = 'Submitted' AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR (\"Status\" = 'Rejected' AND \"RejectionReason\" IS NOT NULL AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR (\"Status\" = 'Approved' AND \"ApprovalJournalEntryId\" IS NOT NULL AND \"PaymentJournalEntryId\" IS NULL) OR (\"Status\" = 'Paid' AND \"ApprovalJournalEntryId\" IS NOT NULL AND \"PaymentJournalEntryId\" IS NOT NULL)");
                    table.CheckConstraint("CK_ExpenseClaims_Total", "\"TotalAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_JournalEntries_ApprovalJournalEntryId_TenantId",
                        columns: x => new { x.ApprovalJournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_JournalEntries_PaymentJournalEntryId_TenantId",
                        columns: x => new { x.PaymentJournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseClaimLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ExpenseClaimId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ExpenseChartOfAccountId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseClaimLines", x => x.Id);
                    table.CheckConstraint("CK_ExpenseClaimLines_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ExpenseClaimLines_ChartOfAccounts_ExpenseChartOfAccountId_C~",
                        columns: x => new { x.ExpenseChartOfAccountId, x.CompanyId, x.TenantId },
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaimLines_ExpenseClaims_ExpenseClaimId_CompanyId_Te~",
                        columns: x => new { x.ExpenseClaimId, x.CompanyId, x.TenantId },
                        principalTable: "ExpenseClaims",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaimLines_ExpenseChartOfAccountId_CompanyId_TenantId",
                table: "ExpenseClaimLines",
                columns: new[] { "ExpenseChartOfAccountId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaimLines_ExpenseClaimId_CompanyId_TenantId",
                table: "ExpenseClaimLines",
                columns: new[] { "ExpenseClaimId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaimLines_TenantId_ExpenseClaimId_LineNumber",
                table: "ExpenseClaimLines",
                columns: new[] { "TenantId", "ExpenseClaimId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_ApprovalJournalEntryId_TenantId",
                table: "ExpenseClaims",
                columns: new[] { "ApprovalJournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_CompanyId_TenantId",
                table: "ExpenseClaims",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_PaymentJournalEntryId_TenantId",
                table: "ExpenseClaims",
                columns: new[] { "PaymentJournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_ExpenseClaims_Company_Number",
                table: "ExpenseClaims",
                columns: new[] { "TenantId", "CompanyId", "ClaimNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseClaimLines");

            migrationBuilder.DropTable(
                name: "ExpenseClaims");
        }
    }
}
