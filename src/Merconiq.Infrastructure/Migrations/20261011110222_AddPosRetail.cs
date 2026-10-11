using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosRetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PosRegisters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosRegisters", x => x.Id);
                    table.UniqueConstraint("AK_PosRegisters_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PosRegisters_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosRegisters_Locations_LocationId_TenantId",
                        columns: x => new { x.LocationId, x.TenantId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    RegisterId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    OverShortValue = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosShifts", x => x.Id);
                    table.UniqueConstraint("AK_PosShifts_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PosShifts_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosShifts_PosRegisters_RegisterId_TenantId",
                        columns: x => new { x.RegisterId, x.TenantId },
                        principalTable: "PosRegisters",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    RegisterId = table.Column<int>(type: "integer", nullable: false),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SoldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SoldBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosSales", x => x.Id);
                    table.UniqueConstraint("AK_PosSales_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PosSales_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSales_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSales_PosRegisters_RegisterId_TenantId",
                        columns: x => new { x.RegisterId, x.TenantId },
                        principalTable: "PosRegisters",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSales_PosShifts_ShiftId_TenantId",
                        columns: x => new { x.ShiftId, x.TenantId },
                        principalTable: "PosShifts",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    RegisterId = table.Column<int>(type: "integer", nullable: false),
                    ShiftId = table.Column<int>(type: "integer", nullable: false),
                    OriginalPosSaleId = table.Column<int>(type: "integer", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RefundedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RefundedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosRefunds", x => x.Id);
                    table.UniqueConstraint("AK_PosRefunds_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PosRefunds_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosRefunds_PosSales_OriginalPosSaleId_TenantId",
                        columns: x => new { x.OriginalPosSaleId, x.TenantId },
                        principalTable: "PosSales",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosSaleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PosSaleId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    LineValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    StockTransactionId = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosSaleLines", x => x.Id);
                    table.CheckConstraint("CK_PosSaleLines_Values", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"LineValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_PosSaleLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSaleLines_PosSales_PosSaleId_TenantId",
                        columns: x => new { x.PosSaleId, x.TenantId },
                        principalTable: "PosSales",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSaleLines_StockTransactions_StockTransactionId_TenantId",
                        columns: x => new { x.StockTransactionId, x.TenantId },
                        principalTable: "StockTransactions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosSaleTenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PosSaleId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosSaleTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosSaleTenders_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSaleTenders_PosSales_PosSaleId_TenantId",
                        columns: x => new { x.PosSaleId, x.TenantId },
                        principalTable: "PosSales",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosRefundLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PosRefundId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PosSaleLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StockTransactionId = table.Column<int>(type: "integer", nullable: true),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefundValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosRefundLines", x => x.Id);
                    table.CheckConstraint("CK_PosRefundLines_Quantity", "\"Quantity\" > 0 AND \"RefundValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_PosRefundLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosRefundLines_PosRefunds_PosRefundId_TenantId",
                        columns: x => new { x.PosRefundId, x.TenantId },
                        principalTable: "PosRefunds",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosRefundLines_PosSaleLines_PosSaleLineId",
                        column: x => x.PosSaleLineId,
                        principalTable: "PosSaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefundLines_CompanyId_TenantId",
                table: "PosRefundLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefundLines_PosRefundId_TenantId",
                table: "PosRefundLines",
                columns: new[] { "PosRefundId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefundLines_PosSaleLineId",
                table: "PosRefundLines",
                column: "PosSaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PosRefundLines_TenantId_PosRefundId_LineNumber",
                table: "PosRefundLines",
                columns: new[] { "TenantId", "PosRefundId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosRefundLines_TenantId_PosSaleLineId",
                table: "PosRefundLines",
                columns: new[] { "TenantId", "PosSaleLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefunds_CompanyId_TenantId",
                table: "PosRefunds",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefunds_OriginalPosSaleId_TenantId",
                table: "PosRefunds",
                columns: new[] { "OriginalPosSaleId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRefunds_TenantId_OriginalPosSaleId",
                table: "PosRefunds",
                columns: new[] { "TenantId", "OriginalPosSaleId" });

            migrationBuilder.CreateIndex(
                name: "UX_PosRefunds_RequestKey",
                table: "PosRefunds",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosRegisters_CompanyId_TenantId",
                table: "PosRegisters",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRegisters_LocationId_TenantId",
                table: "PosRegisters",
                columns: new[] { "LocationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosRegisters_TenantId_CompanyId_Name",
                table: "PosRegisters",
                columns: new[] { "TenantId", "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleLines_CompanyId_TenantId",
                table: "PosSaleLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleLines_PosSaleId_TenantId",
                table: "PosSaleLines",
                columns: new[] { "PosSaleId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleLines_StockTransactionId_TenantId",
                table: "PosSaleLines",
                columns: new[] { "StockTransactionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleLines_TenantId_PosSaleId_LineNumber",
                table: "PosSaleLines",
                columns: new[] { "TenantId", "PosSaleId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleLines_TenantId_StockTransactionId",
                table: "PosSaleLines",
                columns: new[] { "TenantId", "StockTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosSales_CompanyId_TenantId",
                table: "PosSales",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSales_CustomerId",
                table: "PosSales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSales_RegisterId_TenantId",
                table: "PosSales",
                columns: new[] { "RegisterId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSales_ShiftId_TenantId",
                table: "PosSales",
                columns: new[] { "ShiftId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSales_TenantId_ShiftId",
                table: "PosSales",
                columns: new[] { "TenantId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "UX_PosSales_RequestKey",
                table: "PosSales",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleTenders_CompanyId_TenantId",
                table: "PosSaleTenders",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleTenders_PosSaleId_TenantId",
                table: "PosSaleTenders",
                columns: new[] { "PosSaleId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosSaleTenders_TenantId_PosSaleId_LineNumber",
                table: "PosSaleTenders",
                columns: new[] { "TenantId", "PosSaleId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosShifts_CompanyId_TenantId",
                table: "PosShifts",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosShifts_RegisterId_TenantId",
                table: "PosShifts",
                columns: new[] { "RegisterId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PosShifts_TenantId_RegisterId_Status",
                table: "PosShifts",
                columns: new[] { "TenantId", "RegisterId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosRefundLines");

            migrationBuilder.DropTable(
                name: "PosSaleTenders");

            migrationBuilder.DropTable(
                name: "PosRefunds");

            migrationBuilder.DropTable(
                name: "PosSaleLines");

            migrationBuilder.DropTable(
                name: "PosSales");

            migrationBuilder.DropTable(
                name: "PosShifts");

            migrationBuilder.DropTable(
                name: "PosRegisters");
        }
    }
}
