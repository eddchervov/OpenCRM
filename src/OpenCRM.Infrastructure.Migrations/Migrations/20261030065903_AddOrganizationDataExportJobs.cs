using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationDataExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationDataExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ArchiveSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CleanupAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationDataExportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationDataExportJobs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationDataExportJobs_CleanupAfterUtc",
                table: "OrganizationDataExportJobs",
                column: "CleanupAfterUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationDataExportJobs_OrganizationId",
                table: "OrganizationDataExportJobs",
                column: "OrganizationId",
                unique: true,
                filter: "\"State\" IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationDataExportJobs_OrganizationId_RequestedAtUtc",
                table: "OrganizationDataExportJobs",
                columns: new[] { "OrganizationId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationDataExportJobs_State_RequestedAtUtc",
                table: "OrganizationDataExportJobs",
                columns: new[] { "State", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationDataExportJobs");
        }
    }
}
