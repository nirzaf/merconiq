using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_PosSaleTenders_Id_CompanyId_TenantId",
                table: "PosSaleTenders",
                columns: new[] { "Id", "CompanyId", "TenantId" });

            migrationBuilder.CreateTable(
                name: "CardSettlementBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSettlementBatches", x => x.Id);
                    table.UniqueConstraint("AK_CardSettlementBatches_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_CardSettlementBatches_Amounts", "\"GrossAmount\" > 0 AND \"FeeAmount\" >= 0 AND \"FeeAmount\" <= \"GrossAmount\" AND \"NetAmount\" = \"GrossAmount\" - \"FeeAmount\"");
                    table.CheckConstraint("CK_CardSettlementBatches_Reference", "\"Reference\" <> '' AND \"Reference\" = btrim(\"Reference\")");
                    table.ForeignKey(
                        name: "FK_CardSettlementBatches_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardSettlementMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CardSettlementBatchId = table.Column<int>(type: "integer", nullable: false),
                    PosSaleTenderId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnmatchedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UnmatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UnmatchReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardSettlementMatches", x => x.Id);
                    table.CheckConstraint("CK_CardSettlementMatches_Withdrawal", "\"IsActive\" OR (\"UnmatchedAtUtc\" IS NOT NULL AND \"UnmatchReason\" IS NOT NULL AND \"UnmatchReason\" <> '')");
                    table.ForeignKey(
                        name: "FK_CardSettlementMatches_CardSettlementBatches_CardSettlementB~",
                        columns: x => new { x.CardSettlementBatchId, x.CompanyId, x.TenantId },
                        principalTable: "CardSettlementBatches",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardSettlementMatches_PosSaleTenders_PosSaleTenderId_Compan~",
                        columns: x => new { x.PosSaleTenderId, x.CompanyId, x.TenantId },
                        principalTable: "PosSaleTenders",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardSettlementBatches_CompanyId_TenantId",
                table: "CardSettlementBatches",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_CardSettlementBatches_Company_Reference",
                table: "CardSettlementBatches",
                columns: new[] { "TenantId", "CompanyId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardSettlementMatches_CardSettlementBatchId_CompanyId_Tenan~",
                table: "CardSettlementMatches",
                columns: new[] { "CardSettlementBatchId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CardSettlementMatches_PosSaleTenderId_CompanyId_TenantId",
                table: "CardSettlementMatches",
                columns: new[] { "PosSaleTenderId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_CardSettlementMatches_ActiveTender",
                table: "CardSettlementMatches",
                columns: new[] { "TenantId", "CompanyId", "PosSaleTenderId" },
                unique: true,
                filter: "\"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardSettlementMatches");

            migrationBuilder.DropTable(
                name: "CardSettlementBatches");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PosSaleTenders_Id_CompanyId_TenantId",
                table: "PosSaleTenders");
        }
    }
}
