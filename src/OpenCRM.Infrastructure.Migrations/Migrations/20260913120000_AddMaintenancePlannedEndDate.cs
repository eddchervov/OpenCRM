using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary>Добавляет плановую дату окончания без изменения длительности существующих записей.</summary>
    public partial class AddMaintenancePlannedEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PlannedEndDate",
                table: "Maintenance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Maintenance"
                SET "PlannedEndDate" = "PlannedDate"
                WHERE "PlannedDate" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PlannedEndDate", table: "Maintenance");
        }
    }
}
