using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary>Добавляет приёмку и этап съёмки без переклассификации существующих фотографий.</summary>
    public partial class AddMaintenanceIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntakeNotes", table: "Maintenance",
                type: "character varying(4000)", maxLength: 4000, nullable: true);
            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeRecordedAt", table: "Maintenance",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "Stage", table: "MaintenancePhotos",
                type: "integer", nullable: false, defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Stage", table: "MaintenancePhotos");
            migrationBuilder.DropColumn(name: "IntakeRecordedAt", table: "Maintenance");
            migrationBuilder.DropColumn(name: "IntakeNotes", table: "Maintenance");
        }
    }
}
