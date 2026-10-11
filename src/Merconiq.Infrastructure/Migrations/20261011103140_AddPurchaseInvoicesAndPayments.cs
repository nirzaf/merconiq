using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoicesAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PaymentType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_PaymentEntries", x => x.Id);
                    table.UniqueConstraint("AK_PaymentEntries_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PaymentEntries_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentEntries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseInvoices_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PaymentEntryId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    PurchaseInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_PaymentEntries_PaymentEntryId_TenantId",
                        columns: x => new { x.PaymentEntryId, x.TenantId },
                        principalTable: "PaymentEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    GoodsReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MatchedValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_GoodsReceiptLines_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "GoodsReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_PurchaseInvoices_PurchaseInvoiceId_Ten~",
                        columns: x => new { x.PurchaseInvoiceId, x.TenantId },
                        principalTable: "PurchaseInvoices",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_CompanyId_TenantId",
                table: "PaymentAllocations",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentEntryId_TenantId",
                table: "PaymentAllocations",
                columns: new[] { "PaymentEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PurchaseInvoiceId",
                table: "PaymentAllocations",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PaymentEntryId_LineNumber",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PaymentEntryId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEntries_CompanyId_TenantId",
                table: "PaymentEntries",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEntries_CustomerId",
                table: "PaymentEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEntries_SupplierId",
                table: "PaymentEntries",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEntries_TenantId_CompanyId_PostingDate",
                table: "PaymentEntries",
                columns: new[] { "TenantId", "CompanyId", "PostingDate" });

            migrationBuilder.CreateIndex(
                name: "UX_PaymentEntries_RequestKey",
                table: "PaymentEntries",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_CompanyId_TenantId",
                table: "PurchaseInvoiceLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_GoodsReceiptLineId",
                table: "PurchaseInvoiceLines",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseInvoiceId_TenantId",
                table: "PurchaseInvoiceLines",
                columns: new[] { "PurchaseInvoiceId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_TenantId_PurchaseInvoiceId_LineNumber",
                table: "PurchaseInvoiceLines",
                columns: new[] { "TenantId", "PurchaseInvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseInvoiceLines_ReceiptLine",
                table: "PurchaseInvoiceLines",
                columns: new[] { "TenantId", "GoodsReceiptLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_TenantId",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId",
                table: "PurchaseInvoices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_TenantId_CompanyId_DueDate",
                table: "PurchaseInvoices",
                columns: new[] { "TenantId", "CompanyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseInvoices_RequestKey",
                table: "PurchaseInvoices",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseInvoices_SupplierNumber",
                table: "PurchaseInvoices",
                columns: new[] { "TenantId", "CompanyId", "SupplierId", "InvoiceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAllocations");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLines");

            migrationBuilder.DropTable(
                name: "PaymentEntries");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");
        }
    }
}
