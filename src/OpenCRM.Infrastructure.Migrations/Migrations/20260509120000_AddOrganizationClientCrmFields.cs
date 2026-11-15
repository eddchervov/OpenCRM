using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260509120000_AddOrganizationClientCrmFields")]
    public partial class AddOrganizationClientCrmFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OrganizationClients",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleUserId",
                table: "OrganizationClients",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusCode",
                table: "OrganizationClients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId_ResponsibleUserId",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "ResponsibleUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId_StatusCode",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "StatusCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_OrganizationId_ResponsibleUserId",
                table: "OrganizationClients");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_OrganizationId_StatusCode",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "StatusCode",
                table: "OrganizationClients");
        }
    }
}
