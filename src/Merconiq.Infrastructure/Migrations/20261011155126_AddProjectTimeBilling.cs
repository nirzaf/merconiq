using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTimeBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SalesInvoiceLines_DeliveryLine",
                table: "SalesInvoiceLines");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "SalesInvoiceLines",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "DeliveryNoteLineId",
                table: "SalesInvoiceLines",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "TimesheetEntryId",
                table: "SalesInvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_TimesheetEntryId",
                table: "SalesInvoiceLines",
                column: "TimesheetEntryId");

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoiceLines_DeliveryLine",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "DeliveryNoteLineId" },
                unique: true,
                filter: "\"DeliveryNoteLineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoiceLines_TimesheetEntry",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "TimesheetEntryId" },
                unique: true,
                filter: "\"TimesheetEntryId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesInvoiceLines_OneTarget",
                table: "SalesInvoiceLines",
                sql: "(\"DeliveryNoteLineId\" IS NOT NULL)::int + (\"TimesheetEntryId\" IS NOT NULL)::int = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLines_TimesheetEntries_TimesheetEntryId",
                table: "SalesInvoiceLines",
                column: "TimesheetEntryId",
                principalTable: "TimesheetEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLines_TimesheetEntries_TimesheetEntryId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLines_TimesheetEntryId",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "UX_SalesInvoiceLines_DeliveryLine",
                table: "SalesInvoiceLines");

            migrationBuilder.DropIndex(
                name: "UX_SalesInvoiceLines_TimesheetEntry",
                table: "SalesInvoiceLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesInvoiceLines_OneTarget",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "TimesheetEntryId",
                table: "SalesInvoiceLines");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "SalesInvoiceLines",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DeliveryNoteLineId",
                table: "SalesInvoiceLines",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoiceLines_DeliveryLine",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "DeliveryNoteLineId" },
                unique: true);
        }
    }
}
