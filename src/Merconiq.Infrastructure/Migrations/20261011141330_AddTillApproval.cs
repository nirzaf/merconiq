using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTillApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CloseApprovedAtUtc",
                table: "PosShifts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseApprovedBy",
                table: "PosShifts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CloseRejectedAtUtc",
                table: "PosShifts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseRejectedBy",
                table: "PosShifts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloseRejectionReason",
                table: "PosShifts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VarianceApprovalThreshold",
                table: "PosRegisters",
                type: "numeric(18,6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseApprovedAtUtc",
                table: "PosShifts");

            migrationBuilder.DropColumn(
                name: "CloseApprovedBy",
                table: "PosShifts");

            migrationBuilder.DropColumn(
                name: "CloseRejectedAtUtc",
                table: "PosShifts");

            migrationBuilder.DropColumn(
                name: "CloseRejectedBy",
                table: "PosShifts");

            migrationBuilder.DropColumn(
                name: "CloseRejectionReason",
                table: "PosShifts");

            migrationBuilder.DropColumn(
                name: "VarianceApprovalThreshold",
                table: "PosRegisters");
        }
    }
}
