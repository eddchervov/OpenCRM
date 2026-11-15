using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_VehicleOrganizationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OrganizationId",
                table: "Vehicles",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Organizations_OrganizationId",
                table: "Vehicles",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Organizations_OrganizationId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_OrganizationId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Vehicles");
        }
    }
}
