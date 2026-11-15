using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HandedOverAt",
                table: "Maintenance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyForHandoverAt",
                table: "Maintenance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Maintenance_Handover",
                table: "Maintenance",
                sql: "(\"ReadyForHandoverAt\" IS NULL OR \"Status\" = 3) AND (\"HandedOverAt\" IS NULL OR (\"ReadyForHandoverAt\" IS NOT NULL AND \"HandedOverAt\" >= \"ReadyForHandoverAt\"))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Maintenance_Handover",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "HandedOverAt",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "ReadyForHandoverAt",
                table: "Maintenance");
        }
    }
}
