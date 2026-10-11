using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillOfMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterials", x => x.Id);
                    table.UniqueConstraint("AK_BillOfMaterials_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillOfMaterials_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillOfMaterialsLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BillOfMaterialsId = table.Column<int>(type: "integer", nullable: false),
                    ComponentItemId = table.Column<int>(type: "integer", nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterialsLines", x => x.Id);
                    table.CheckConstraint("CK_BillOfMaterialsLines_Quantity", "\"QuantityPerUnit\" > 0");
                    table.ForeignKey(
                        name: "FK_BillOfMaterialsLines_BillOfMaterials_BillOfMaterialsId_Tena~",
                        columns: x => new { x.BillOfMaterialsId, x.TenantId },
                        principalTable: "BillOfMaterials",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillOfMaterialsLines_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillOfMaterialsLines_Items_ComponentItemId",
                        column: x => x.ComponentItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BillOfMaterialsId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BomVersion = table.Column<int>(type: "integer", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConsumedCost = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrders_Id_TenantId", x => new { x.Id, x.TenantId });
                    table.ForeignKey(
                        name: "FK_WorkOrders_BillOfMaterials_BillOfMaterialsId",
                        column: x => x.BillOfMaterialsId,
                        principalTable: "BillOfMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Locations_LocationId_TenantId",
                        columns: x => new { x.LocationId, x.TenantId },
                        principalTable: "Locations",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderConsumptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<int>(type: "integer", nullable: false),
                    ComponentItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StockTransactionId = table.Column<int>(type: "integer", nullable: false),
                    ConsumedValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderConsumptions", x => x.Id);
                    table.CheckConstraint("CK_WorkOrderConsumptions_Quantity", "\"Quantity\" > 0 AND \"ConsumedValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_WorkOrderConsumptions_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderConsumptions_WorkOrders_WorkOrderId_TenantId",
                        columns: x => new { x.WorkOrderId, x.TenantId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_CompanyId_TenantId",
                table: "BillOfMaterials",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterials_ItemId",
                table: "BillOfMaterials",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "UX_BillOfMaterials_ItemVersion",
                table: "BillOfMaterials",
                columns: new[] { "TenantId", "CompanyId", "ItemId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsLines_BillOfMaterialsId_TenantId",
                table: "BillOfMaterialsLines",
                columns: new[] { "BillOfMaterialsId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsLines_CompanyId_TenantId",
                table: "BillOfMaterialsLines",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsLines_ComponentItemId",
                table: "BillOfMaterialsLines",
                column: "ComponentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialsLines_TenantId_BillOfMaterialsId_LineNumber",
                table: "BillOfMaterialsLines",
                columns: new[] { "TenantId", "BillOfMaterialsId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderConsumptions_CompanyId_TenantId",
                table: "WorkOrderConsumptions",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderConsumptions_TenantId_StockTransactionId",
                table: "WorkOrderConsumptions",
                columns: new[] { "TenantId", "StockTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderConsumptions_TenantId_WorkOrderId_ComponentItemId",
                table: "WorkOrderConsumptions",
                columns: new[] { "TenantId", "WorkOrderId", "ComponentItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderConsumptions_WorkOrderId_TenantId",
                table: "WorkOrderConsumptions",
                columns: new[] { "WorkOrderId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_BillOfMaterialsId",
                table: "WorkOrders",
                column: "BillOfMaterialsId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CompanyId_TenantId",
                table: "WorkOrders",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_LocationId_TenantId",
                table: "WorkOrders",
                columns: new[] { "LocationId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_CompanyId_Status",
                table: "WorkOrders",
                columns: new[] { "TenantId", "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_CompanyId_WorkOrderNumber",
                table: "WorkOrders",
                columns: new[] { "TenantId", "CompanyId", "WorkOrderNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillOfMaterialsLines");

            migrationBuilder.DropTable(
                name: "WorkOrderConsumptions");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "BillOfMaterials");
        }
    }
}
