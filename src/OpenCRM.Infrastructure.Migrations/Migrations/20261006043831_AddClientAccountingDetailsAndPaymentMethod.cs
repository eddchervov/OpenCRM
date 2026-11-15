using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAccountingDetailsAndPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Inn",
                table: "OrganizationClients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kpp",
                table: "OrganizationClients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalAddress",
                table: "OrganizationClients",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyTypeCode",
                table: "OrganizationClients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MethodCode",
                table: "MaintenancePayments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Inn",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "Kpp",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "LegalAddress",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "PartyTypeCode",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MethodCode",
                table: "MaintenancePayments");
        }
    }
}
