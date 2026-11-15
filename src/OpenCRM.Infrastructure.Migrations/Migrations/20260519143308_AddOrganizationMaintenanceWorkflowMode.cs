using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMaintenanceWorkflowMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaintenanceWorkflowMode",
                table: "Organizations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "staged");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaintenanceWorkflowMode",
                table: "Organizations");
        }
    }
}
