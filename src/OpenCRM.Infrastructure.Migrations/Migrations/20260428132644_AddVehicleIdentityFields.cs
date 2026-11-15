using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyNumber",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineNumber",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationPlate",
                table: "Vehicles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vin",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyNumber",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "EngineNumber",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RegistrationPlate",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Vin",
                table: "Vehicles");
        }
    }
}
