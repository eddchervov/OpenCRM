using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_OrganizationVehicleCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentMileage",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentMileageUpdatedAt",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VehicleCounterEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCounterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleCounterEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleCounterEntries_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VehicleCounterEntries_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_CurrentMileageUpdatedAt",
                table: "Vehicles",
                column: "CurrentMileageUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCounterEntries_MaintenanceId",
                table: "VehicleCounterEntries",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCounterEntries_RecordedAt",
                table: "VehicleCounterEntries",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCounterEntries_UserId",
                table: "VehicleCounterEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCounterEntries_VehicleId",
                table: "VehicleCounterEntries",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCounterEntries_VehicleId_RecordedAt_IsDeleted",
                table: "VehicleCounterEntries",
                columns: new[] { "VehicleId", "RecordedAt", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleCounterEntries");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_CurrentMileageUpdatedAt",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CurrentMileage",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CurrentMileageUpdatedAt",
                table: "Vehicles");
        }
    }
}
