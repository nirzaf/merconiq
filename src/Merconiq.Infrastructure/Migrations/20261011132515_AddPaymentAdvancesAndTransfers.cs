using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAdvancesAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromAccountId",
                table: "PaymentEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdvance",
                table: "PaymentEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToAccountId",
                table: "PaymentEntries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromAccountId",
                table: "PaymentEntries");

            migrationBuilder.DropColumn(
                name: "IsAdvance",
                table: "PaymentEntries");

            migrationBuilder.DropColumn(
                name: "ToAccountId",
                table: "PaymentEntries");
        }
    }
}
