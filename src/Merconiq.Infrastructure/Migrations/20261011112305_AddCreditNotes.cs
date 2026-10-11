using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseCreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_PurchaseCreditNotes", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseCreditNotes_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNotes_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNotes_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesCreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CreditNoteNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_SalesCreditNotes", x => x.Id);
                    table.UniqueConstraint("AK_SalesCreditNotes_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_SalesCreditNotes_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesCreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseCreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseCreditNoteId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    GoodsReceiptLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreditValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseCreditNoteLines", x => x.Id);
                    table.CheckConstraint("CK_PurchaseCreditNoteLines_Quantity", "\"Quantity\" > 0 AND \"CreditValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNoteLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNoteLines_GoodsReceiptLines_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "GoodsReceiptLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseCreditNoteLines_PurchaseCreditNotes_PurchaseCreditN~",
                        columns: x => new { x.PurchaseCreditNoteId, x.TenantId },
                        principalTable: "PurchaseCreditNotes",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesCreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesCreditNoteId = table.Column<int>(type: "integer", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    DeliveryNoteLineId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StockTransactionId = table.Column<int>(type: "integer", nullable: true),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreditValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCreditNoteLines", x => x.Id);
                    table.CheckConstraint("CK_SalesCreditNoteLines_Quantity", "\"Quantity\" > 0 AND \"CreditValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLines_DeliveryNoteLines_DeliveryNoteLineId",
                        column: x => x.DeliveryNoteLineId,
                        principalTable: "DeliveryNoteLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesCreditNoteLines_SalesCreditNotes_SalesCreditNoteId_Ten~",
                        columns: x => new { x.SalesCreditNoteId, x.TenantId },
                        principalTable: "SalesCreditNotes",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLines_CompanyId_TenantId",
                table: "PurchaseCreditNoteLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLines_GoodsReceiptLineId",
                table: "PurchaseCreditNoteLines",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLines_PurchaseCreditNoteId_TenantId",
                table: "PurchaseCreditNoteLines",
                columns: new[] { "PurchaseCreditNoteId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLines_TenantId_GoodsReceiptLineId",
                table: "PurchaseCreditNoteLines",
                columns: new[] { "TenantId", "GoodsReceiptLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNoteLines_TenantId_PurchaseCreditNoteId_LineN~",
                table: "PurchaseCreditNoteLines",
                columns: new[] { "TenantId", "PurchaseCreditNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNotes_CompanyId_TenantId",
                table: "PurchaseCreditNotes",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseCreditNotes_SupplierId",
                table: "PurchaseCreditNotes",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseCreditNotes_RequestKey",
                table: "PurchaseCreditNotes",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseCreditNotes_SupplierNumber",
                table: "PurchaseCreditNotes",
                columns: new[] { "TenantId", "CompanyId", "SupplierId", "CreditNoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_CompanyId_TenantId",
                table: "SalesCreditNoteLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_DeliveryNoteLineId",
                table: "SalesCreditNoteLines",
                column: "DeliveryNoteLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_SalesCreditNoteId_TenantId",
                table: "SalesCreditNoteLines",
                columns: new[] { "SalesCreditNoteId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_TenantId_DeliveryNoteLineId",
                table: "SalesCreditNoteLines",
                columns: new[] { "TenantId", "DeliveryNoteLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNoteLines_TenantId_SalesCreditNoteId_LineNumber",
                table: "SalesCreditNoteLines",
                columns: new[] { "TenantId", "SalesCreditNoteId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNotes_CompanyId_TenantId",
                table: "SalesCreditNotes",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCreditNotes_CustomerId",
                table: "SalesCreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_SalesCreditNotes_Number",
                table: "SalesCreditNotes",
                columns: new[] { "TenantId", "CompanyId", "CreditNoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SalesCreditNotes_RequestKey",
                table: "SalesCreditNotes",
                columns: new[] { "TenantId", "CompanyId", "RequestKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseCreditNoteLines");

            migrationBuilder.DropTable(
                name: "SalesCreditNoteLines");

            migrationBuilder.DropTable(
                name: "PurchaseCreditNotes");

            migrationBuilder.DropTable(
                name: "SalesCreditNotes");
        }
    }
}
