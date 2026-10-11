using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCreditLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditLimitOverrideBy",
                table: "SalesInvoices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditLimitOverrideReason",
                table: "SalesInvoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditLimitChangeReason",
                table: "Customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreditLimitChangedAtUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditLimitChangedBy",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CreditLimit",
                table: "Customers",
                sql: "\"CreditLimit\" IS NULL OR \"CreditLimit\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CreditLimit",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreditLimitOverrideBy",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CreditLimitOverrideReason",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreditLimitChangeReason",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreditLimitChangedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CreditLimitChangedBy",
                table: "Customers");
        }
    }
}
