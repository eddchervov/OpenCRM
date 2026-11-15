using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_vehicle_mileage_unit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MileageUnit",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_MileageUnit",
                table: "Vehicles",
                column: "MileageUnit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_MileageUnit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "MileageUnit",
                table: "Vehicles");
        }
    }
}
