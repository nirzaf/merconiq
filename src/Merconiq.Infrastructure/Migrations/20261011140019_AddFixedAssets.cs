using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFixedAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    AssetCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DepreciationStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CostAmount = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    SalvageValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    UsefulLifeMonths = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CapitalizationJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    CapitalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapitalizedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisposalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DisposalProceeds = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    DisposalJournalEntryId = table.Column<int>(type: "integer", nullable: true),
                    DisposedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisposedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisposalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.UniqueConstraint("AK_FixedAssets_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_FixedAssets_Code", "\"AssetCode\" <> '' AND \"AssetCode\" = btrim(\"AssetCode\")");
                    table.CheckConstraint("CK_FixedAssets_Lifecycle", "(\"Status\" = 'Registered' AND \"CapitalizationJournalEntryId\" IS NULL AND \"DisposalJournalEntryId\" IS NULL) OR (\"Status\" = 'Capitalized' AND \"CapitalizationJournalEntryId\" IS NOT NULL AND \"DisposalJournalEntryId\" IS NULL) OR (\"Status\" = 'Disposed' AND \"CapitalizationJournalEntryId\" IS NOT NULL AND \"DisposalJournalEntryId\" IS NOT NULL)");
                    table.CheckConstraint("CK_FixedAssets_Name", "\"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                    table.CheckConstraint("CK_FixedAssets_Schedule", "\"CostAmount\" > 0 AND \"SalvageValue\" >= 0 AND \"SalvageValue\" < \"CostAmount\" AND \"UsefulLifeMonths\" BETWEEN 1 AND 600 AND \"DepreciationStartDate\" >= \"AcquisitionDate\"");
                    table.ForeignKey(
                        name: "FK_FixedAssets_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_JournalEntries_CapitalizationJournalEntryId_Ten~",
                        columns: x => new { x.CapitalizationJournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FixedAssets_JournalEntries_DisposalJournalEntryId_TenantId",
                        columns: x => new { x.DisposalJournalEntryId, x.TenantId },
                        principalTable: "JournalEntries",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CapitalizationJournalEntryId_TenantId",
                table: "FixedAssets",
                columns: new[] { "CapitalizationJournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CompanyId_TenantId",
                table: "FixedAssets",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_DisposalJournalEntryId_TenantId",
                table: "FixedAssets",
                columns: new[] { "DisposalJournalEntryId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_FixedAssets_Company_Code",
                table: "FixedAssets",
                columns: new[] { "TenantId", "CompanyId", "AssetCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixedAssets");
        }
    }
}
