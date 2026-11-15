using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceWorkActNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkActOrganizationId",
                table: "Maintenance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkActNumber",
                table: "Maintenance",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Maintenance_WorkActOrganizationId_WorkActNumber",
                table: "Maintenance",
                columns: new[] { "WorkActOrganizationId", "WorkActNumber" },
                unique: true,
                filter: "\"WorkActOrganizationId\" IS NOT NULL AND \"WorkActNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Maintenance_WorkActOrganizationId_WorkActNumber",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "WorkActOrganizationId",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "WorkActNumber",
                table: "Maintenance");
        }
    }
}
