using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSchedulerTaskState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSchedulerTaskStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StatusCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeferredUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSchedulerTaskStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTaskStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTaskStates_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTaskStates_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTaskStates_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_MaintenanceId",
                table: "OrganizationSchedulerTaskStates",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_Organization_Trigger",
                table: "OrganizationSchedulerTaskStates",
                columns: new[] { "OrganizationId", "TriggerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_OrganizationId",
                table: "OrganizationSchedulerTaskStates",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_StatusCode",
                table: "OrganizationSchedulerTaskStates",
                column: "StatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_UserId",
                table: "OrganizationSchedulerTaskStates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_VehicleId",
                table: "OrganizationSchedulerTaskStates",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSchedulerTaskStates");
        }
    }
}
