using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_OrganizationClientsLight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationClientId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Comment = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationClients_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OrganizationClientId",
                table: "Vehicles",
                column: "OrganizationClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId",
                table: "OrganizationClients",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId_Name",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_OrganizationClients_OrganizationClientId",
                table: "Vehicles",
                column: "OrganizationClientId",
                principalTable: "OrganizationClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_OrganizationClients_OrganizationClientId",
                table: "Vehicles");

            migrationBuilder.DropTable(
                name: "OrganizationClients");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_OrganizationClientId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OrganizationClientId",
                table: "Vehicles");
        }
    }
}
