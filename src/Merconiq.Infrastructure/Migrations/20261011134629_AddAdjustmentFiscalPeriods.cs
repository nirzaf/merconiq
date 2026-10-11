using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustmentFiscalPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiscalPeriodId",
                table: "JournalEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdjustment",
                table: "FiscalPeriods",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_FiscalPeriodId_CompanyId_TenantId",
                table: "JournalEntries",
                columns: new[] { "FiscalPeriodId", "CompanyId", "TenantId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FiscalPeriods_Adjustment",
                table: "FiscalPeriods",
                sql: "NOT \"IsAdjustment\" OR (\"PeriodNumber\" = 13 AND \"EndDate\" = \"StartDate\")");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_FiscalPeriods_FiscalPeriodId_CompanyId_Tenan~",
                table: "JournalEntries",
                columns: new[] { "FiscalPeriodId", "CompanyId", "TenantId" },
                principalTable: "FiscalPeriods",
                principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_FiscalPeriods_FiscalPeriodId_CompanyId_Tenan~",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_FiscalPeriodId_CompanyId_TenantId",
                table: "JournalEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FiscalPeriods_Adjustment",
                table: "FiscalPeriods");

            migrationBuilder.DropColumn(
                name: "FiscalPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "IsAdjustment",
                table: "FiscalPeriods");
        }
    }
}
