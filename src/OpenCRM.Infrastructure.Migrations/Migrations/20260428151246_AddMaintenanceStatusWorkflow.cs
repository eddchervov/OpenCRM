using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceStatusWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedDate",
                table: "Maintenance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedDate",
                table: "Maintenance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Maintenance",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.Sql("""
                UPDATE "Maintenance"
                SET "Status" = 3,
                    "CompletedDate" = "Date"
                WHERE "CompletedDate" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedDate",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "PlannedDate",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Maintenance");
        }
    }
}
