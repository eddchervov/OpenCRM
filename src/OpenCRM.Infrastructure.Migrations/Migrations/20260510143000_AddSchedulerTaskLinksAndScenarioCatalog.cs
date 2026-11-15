using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260510143000_AddSchedulerTaskLinksAndScenarioCatalog")]
    public partial class AddSchedulerTaskLinksAndScenarioCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedDefectId",
                table: "OrganizationSchedulerTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceCode",
                table: "OrganizationSchedulerTasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                table: "OrganizationSchedulerTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceUntil",
                table: "OrganizationSchedulerTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderAt",
                table: "OrganizationSchedulerTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "OrganizationSchedulerTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationScenarioCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    KindCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationScenarioCatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationScenarioCatalogItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_LinkedDefectId",
                table: "OrganizationSchedulerTasks",
                column: "LinkedDefectId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks",
                column: "LinkedMaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_LinkedDefectId",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "LinkedDefectId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "LinkedMaintenanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_ReminderAt_ReminderSentAt",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "ReminderAt", "ReminderSentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationScenarioCatalogItems_OrganizationId_KindCode_Code",
                table: "OrganizationScenarioCatalogItems",
                columns: new[] { "OrganizationId", "KindCode", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationScenarioCatalogItems_OrganizationId_KindCode_IsActive_SortOrder",
                table: "OrganizationScenarioCatalogItems",
                columns: new[] { "OrganizationId", "KindCode", "IsActive", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationSchedulerTasks_Maintenance_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks",
                column: "LinkedMaintenanceId",
                principalTable: "Maintenance",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationSchedulerTasks_VehicleDefects_LinkedDefectId",
                table: "OrganizationSchedulerTasks",
                column: "LinkedDefectId",
                principalTable: "VehicleDefects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationSchedulerTasks_Maintenance_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationSchedulerTasks_VehicleDefects_LinkedDefectId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropTable(
                name: "OrganizationScenarioCatalogItems");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_LinkedDefectId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_LinkedDefectId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_ReminderAt_ReminderSentAt",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "LinkedDefectId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "LinkedMaintenanceId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceCode",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceUntil",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "ReminderAt",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "OrganizationSchedulerTasks");
        }
    }
}
