using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260509130000_AddOrganizationClientTags")]
    public partial class AddOrganizationClientTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationClientTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationClientTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationClientTags_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationClientTagAssignments",
                columns: table => new
                {
                    OrganizationClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationClientTagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationClientTagAssignments", x => new { x.OrganizationClientId, x.OrganizationClientTagId });
                    table.ForeignKey(
                        name: "FK_OrganizationClientTagAssignments_OrganizationClientTags_OrganizationClientTagId",
                        column: x => x.OrganizationClientTagId,
                        principalTable: "OrganizationClientTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationClientTagAssignments_OrganizationClients_OrganizationClientId",
                        column: x => x.OrganizationClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientTagAssignments_OrganizationClientTagId",
                table: "OrganizationClientTagAssignments",
                column: "OrganizationClientTagId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientTags_OrganizationId_NormalizedName",
                table: "OrganizationClientTags",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationClientTagAssignments");

            migrationBuilder.DropTable(
                name: "OrganizationClientTags");
        }
    }
}
