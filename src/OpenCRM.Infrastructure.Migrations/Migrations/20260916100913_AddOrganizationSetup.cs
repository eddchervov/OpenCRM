using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SetupCompletedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetupRevision",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SetupStep",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkdayEnd",
                table: "Organizations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkdayStart",
                table: "Organizations",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "WorkingDays",
                table: "Organizations",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::integer[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupCompletedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SetupRevision",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SetupStep",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkdayEnd",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkdayStart",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "WorkingDays",
                table: "Organizations");
        }
    }
}
