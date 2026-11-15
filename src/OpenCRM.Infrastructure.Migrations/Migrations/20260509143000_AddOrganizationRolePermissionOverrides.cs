using System;
using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260509143000_AddOrganizationRolePermissionOverrides")]
    public partial class AddOrganizationRolePermissionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationRolePermissionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRolePermissionOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationRolePermissionOverrides_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRolePermissionOverrides_OrganizationId_RoleCode_PermissionCode",
                table: "OrganizationRolePermissionOverrides",
                columns: new[] { "OrganizationId", "RoleCode", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRolePermissionOverrides_PermissionCode",
                table: "OrganizationRolePermissionOverrides",
                column: "PermissionCode");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRolePermissionOverrides_RoleCode",
                table: "OrganizationRolePermissionOverrides",
                column: "RoleCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationRolePermissionOverrides");
        }
    }
}
