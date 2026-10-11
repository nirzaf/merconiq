using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderToCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceId",
                table: "PaymentAllocations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "SalesInvoiceId",
                table: "PaymentAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SalesInvoices_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrencyScale = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrders_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_SalesOrders_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNotes", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryNotes_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_Locations_LocationId_TenantId",
                        columns: x => new { x.LocationId, x.TenantId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNotes_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    NetValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderLines_Quantities", "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"NetValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_SalesOrderId_TenantId",
                        columns: x => new { x.SalesOrderId, x.TenantId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryNoteId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StockTransactionId = table.Column<int>(type: "integer", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryNoteLines", x => x.Id);
                    table.CheckConstraint("CK_DeliveryNoteLines_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_DeliveryNoteLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteLines_DeliveryNotes_DeliveryNoteId_TenantId",
                        columns: x => new { x.DeliveryNoteId, x.TenantId },
                        principalTable: "DeliveryNotes",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryNoteLines_StockTransactions_StockTransactionId_Tena~",
                        columns: x => new { x.StockTransactionId, x.TenantId },
                        principalTable: "StockTransactions",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    DeliveryNoteLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    RevenueValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_DeliveryNoteLines_DeliveryNoteLineId",
                        column: x => x.DeliveryNoteLineId,
                        principalTable: "DeliveryNoteLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_SalesInvoiceId_TenantId",
                        columns: x => new { x.SalesInvoiceId, x.TenantId },
                        principalTable: "SalesInvoices",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_SalesInvoiceId",
                table: "PaymentAllocations",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PurchaseInvoiceId" },
                filter: "\"PurchaseInvoiceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_SalesInvoiceId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "SalesInvoiceId" },
                filter: "\"SalesInvoiceId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAllocations_OneTarget",
                table: "PaymentAllocations",
                sql: "(\"PurchaseInvoiceId\" IS NOT NULL)::int + (\"SalesInvoiceId\" IS NOT NULL)::int = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_CompanyId_TenantId",
                table: "DeliveryNoteLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_DeliveryNoteId_TenantId",
                table: "DeliveryNoteLines",
                columns: new[] { "DeliveryNoteId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_SalesOrderLineId",
                table: "DeliveryNoteLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_StockTransactionId_TenantId",
                table: "DeliveryNoteLines",
                columns: new[] { "StockTransactionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_TenantId_DeliveryNoteId_LineNumber",
                table: "DeliveryNoteLines",
                columns: new[] { "TenantId", "DeliveryNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_TenantId_SalesOrderLineId",
                table: "DeliveryNoteLines",
                columns: new[] { "TenantId", "SalesOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNoteLines_TenantId_StockTransactionId",
                table: "DeliveryNoteLines",
                columns: new[] { "TenantId", "StockTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_CompanyId_TenantId",
                table: "DeliveryNotes",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_LocationId_TenantId",
                table: "DeliveryNotes",
                columns: new[] { "LocationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_SalesOrderId",
                table: "DeliveryNotes",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryNotes_TenantId_CompanyId_SalesOrderId",
                table: "DeliveryNotes",
                columns: new[] { "TenantId", "CompanyId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "UX_DeliveryNotes_RequestKey",
                table: "DeliveryNotes",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_CompanyId_TenantId",
                table: "SalesInvoiceLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_DeliveryNoteLineId",
                table: "SalesInvoiceLines",
                column: "DeliveryNoteLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_SalesInvoiceId_TenantId",
                table: "SalesInvoiceLines",
                columns: new[] { "SalesInvoiceId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_TenantId_SalesInvoiceId_LineNumber",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "SalesInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoiceLines_DeliveryLine",
                table: "SalesInvoiceLines",
                columns: new[] { "TenantId", "DeliveryNoteLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_TenantId",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CustomerId",
                table: "SalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_CompanyId_DueDate",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CompanyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoices_InvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesInvoices_RequestKey",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_TenantId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_TenantId",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_TenantId_SalesOrderId_LineNumber",
                table: "SalesOrderLines",
                columns: new[] { "TenantId", "SalesOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_TenantId",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "SalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_TenantId_CompanyId_OrderNumber",
                table: "SalesOrders",
                columns: new[] { "TenantId", "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesOrders_RequestKey",
                table: "SalesOrders",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_SalesInvoices_SalesInvoiceId",
                table: "PaymentAllocations",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_SalesInvoices_SalesInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "DeliveryNoteLines");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "DeliveryNotes");

            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_SalesInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_TenantId_SalesInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAllocations_OneTarget",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId",
                table: "PaymentAllocations");

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceId",
                table: "PaymentAllocations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PurchaseInvoiceId" });
        }
    }
}
