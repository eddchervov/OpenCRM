using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class LinkOrganizationEmployeesToMembers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "MemberId",
            table: "OrganizationEmployees",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrganizationEmployees_MemberId",
            table: "OrganizationEmployees",
            column: "MemberId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrganizationEmployees_OrganizationId_MemberId",
            table: "OrganizationEmployees",
            columns: new[] { "OrganizationId", "MemberId" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_OrganizationMembers_OrganizationId_Id",
            table: "OrganizationMembers",
            columns: new[] { "OrganizationId", "Id" });

        migrationBuilder.AddForeignKey(
            name: "FK_OrganizationEmployees_OrganizationMembers_OrganizationId_MemberId",
            table: "OrganizationEmployees",
            columns: new[] { "OrganizationId", "MemberId" },
            principalTable: "OrganizationMembers",
            principalColumns: new[] { "OrganizationId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OrganizationEmployees_OrganizationMembers_OrganizationId_MemberId",
            table: "OrganizationEmployees");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_OrganizationMembers_OrganizationId_Id",
            table: "OrganizationMembers");

        migrationBuilder.DropIndex(
            name: "IX_OrganizationEmployees_MemberId",
            table: "OrganizationEmployees");

        migrationBuilder.DropIndex(
            name: "IX_OrganizationEmployees_OrganizationId_MemberId",
            table: "OrganizationEmployees");

        migrationBuilder.DropColumn(
            name: "MemberId",
            table: "OrganizationEmployees");
    }
}
