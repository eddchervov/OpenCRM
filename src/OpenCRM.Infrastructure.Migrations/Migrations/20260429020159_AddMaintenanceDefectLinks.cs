using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceDefectLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenanceDefects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleDefectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceDefects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceDefects_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceDefects_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceDefects_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceDefects_VehicleDefects_VehicleDefectId",
                        column: x => x.VehicleDefectId,
                        principalTable: "VehicleDefects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDefects_MaintenanceId",
                table: "MaintenanceDefects",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDefects_MaintenanceId_VehicleDefectId",
                table: "MaintenanceDefects",
                columns: new[] { "MaintenanceId", "VehicleDefectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDefects_OrganizationId",
                table: "MaintenanceDefects",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDefects_UserId",
                table: "MaintenanceDefects",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDefects_VehicleDefectId",
                table: "MaintenanceDefects",
                column: "VehicleDefectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceDefects");
        }
    }
}
