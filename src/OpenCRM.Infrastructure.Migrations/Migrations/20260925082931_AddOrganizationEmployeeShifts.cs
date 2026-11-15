using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationEmployeeShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShiftTimeZoneId",
                table: "Organizations",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrganizationEmployees_OrganizationId_Id",
                table: "OrganizationEmployees",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "OrganizationEmployeeShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlanStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlanEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PlannedBreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEmployeeShifts", x => x.Id);
                    table.CheckConstraint("CK_OrganizationEmployeeShifts_Plan", "\"PlanStartUtc\" < \"PlanEndUtc\" AND \"PlannedBreakMinutes\" >= 0");
                    table.ForeignKey(
                        name: "FK_OrganizationEmployeeShifts_OrganizationEmployees_Organizati~",
                        columns: x => new { x.OrganizationId, x.EmployeeId },
                        principalTable: "OrganizationEmployees",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationEmployeeShifts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmployeeShifts_OrganizationId_EmployeeId_PlanSt~",
                table: "OrganizationEmployeeShifts",
                columns: new[] { "OrganizationId", "EmployeeId", "PlanStartUtc", "PlanEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmployeeShifts_OrganizationId_LocalStartDate_Em~",
                table: "OrganizationEmployeeShifts",
                columns: new[] { "OrganizationId", "LocalStartDate", "EmployeeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationEmployeeShifts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrganizationEmployees_OrganizationId_Id",
                table: "OrganizationEmployees");

            migrationBuilder.DropColumn(
                name: "ShiftTimeZoneId",
                table: "Organizations");
        }
    }
}
