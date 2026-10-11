using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Merconiq.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_Id_CompanyId_TenantId",
                table: "Customers",
                columns: new[] { "Id", "CompanyId", "TenantId" });

            migrationBuilder.CreateTable(
                name: "CrmLeads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisqualifiedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLeads", x => x.Id);
                    table.UniqueConstraint("AK_CrmLeads_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_CrmLeads_Disqualified", "(\"Status\" = 'Disqualified') = (\"DisqualifiedReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CrmLeads_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmOpportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LeadId = table.Column<int>(type: "integer", nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpectedValue = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ExpectedCloseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LostReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmOpportunities", x => x.Id);
                    table.UniqueConstraint("AK_CrmOpportunities_Id_CompanyId_TenantId", x => new { x.Id, x.CompanyId, x.TenantId });
                    table.CheckConstraint("CK_CrmOpportunities_Value", "\"ExpectedValue\" >= 0");
                    table.CheckConstraint("CK_CrmOpportunities_Won", "(\"Stage\" = 'Won') = (\"CustomerId\" IS NOT NULL) AND (\"Stage\" = 'Lost') = (\"LostReason\" IS NOT NULL) AND (\"Stage\" IN ('Won', 'Lost')) = (\"ClosedAtUtc\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmLeads_LeadId_CompanyId_TenantId",
                        columns: x => new { x.LeadId, x.CompanyId, x.TenantId },
                        principalTable: "CrmLeads",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_Customers_CustomerId_CompanyId_TenantId",
                        columns: x => new { x.CustomerId, x.CompanyId, x.TenantId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    LeadId = table.Column<int>(type: "integer", nullable: true),
                    OpportunityId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmActivities", x => x.Id);
                    table.CheckConstraint("CK_CrmActivities_OneTarget", "(\"LeadId\" IS NULL) <> (\"OpportunityId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CrmActivities_Companies_CompanyId_TenantId",
                        columns: x => new { x.CompanyId, x.TenantId },
                        principalTable: "Companies",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmActivities_CrmLeads_LeadId_CompanyId_TenantId",
                        columns: x => new { x.LeadId, x.CompanyId, x.TenantId },
                        principalTable: "CrmLeads",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmActivities_CrmOpportunities_OpportunityId_CompanyId_Tena~",
                        columns: x => new { x.OpportunityId, x.CompanyId, x.TenantId },
                        principalTable: "CrmOpportunities",
                        principalColumns: new[] { "Id", "CompanyId", "TenantId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_CompanyId_TenantId",
                table: "CrmActivities",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_LeadId_CompanyId_TenantId",
                table: "CrmActivities",
                columns: new[] { "LeadId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_OpportunityId_CompanyId_TenantId",
                table: "CrmActivities",
                columns: new[] { "OpportunityId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_CompanyId_TenantId",
                table: "CrmLeads",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_CompanyId_TenantId",
                table: "CrmOpportunities",
                columns: new[] { "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_CustomerId_CompanyId_TenantId",
                table: "CrmOpportunities",
                columns: new[] { "CustomerId", "CompanyId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_LeadId_CompanyId_TenantId",
                table: "CrmOpportunities",
                columns: new[] { "LeadId", "CompanyId", "TenantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmActivities");

            migrationBuilder.DropTable(
                name: "CrmOpportunities");

            migrationBuilder.DropTable(
                name: "CrmLeads");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_Id_CompanyId_TenantId",
                table: "Customers");
        }
    }
}
