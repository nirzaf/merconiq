using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalPeriodAccountLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_FiscalPeriods_Id_CompanyId_TenantId",
                table: "FiscalPeriods",
                columns: new[] { "Id", "CompanyId", "TenantId" });

            migrationBuilder.CreateTable(
                name: "FiscalPeriodAccountLocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FiscalPeriodId = table.Column<int>(type: "integer", nullable: false),
                    ChartOfAccountId = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReleaseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriodAccountLocks", x => x.Id);
                    table.CheckConstraint("CK_FiscalPeriodAccountLocks_Reasons", "\"LockReason\" <> '' AND \"LockReason\" = btrim(\"LockReason\") AND (\"ReleaseReason\" IS NULL OR (\"ReleaseReason\" <> '' AND \"ReleaseReason\" = btrim(\"ReleaseReason\")))");
                    table.ForeignKey(
                        name: "FK_FiscalPeriodAccountLocks_ChartOfAccounts_ChartOfAccountId_C~",
                        columns: x => new { x.ChartOfAccountId, x.CompanyId, x.TenantId },
                        principalTable: "ChartOfAccounts",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalPeriodAccountLocks_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FiscalPeriodAccountLocks_FiscalPeriods_FiscalPeriodId_Compa~",
                        columns: x => new { x.FiscalPeriodId, x.CompanyId, x.TenantId },
                        principalTable: "FiscalPeriods",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriodAccountLocks_ChartOfAccountId_CompanyId_TenantId",
                table: "FiscalPeriodAccountLocks",
                columns: new[] { "ChartOfAccountId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriodAccountLocks_CompanyId_TenantId",
                table: "FiscalPeriodAccountLocks",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriodAccountLocks_FiscalPeriodId_CompanyId_TenantId",
                table: "FiscalPeriodAccountLocks",
                columns: new[] { "FiscalPeriodId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "UX_FiscalPeriodAccountLocks_Period_Account",
                table: "FiscalPeriodAccountLocks",
                columns: new[] { "TenantId", "CompanyId", "FiscalPeriodId", "ChartOfAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalPeriodAccountLocks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FiscalPeriods_Id_CompanyId_TenantId",
                table: "FiscalPeriods");
        }
    }
}
