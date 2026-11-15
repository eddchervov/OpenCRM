using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    public partial class AddUniquePendingOrganizationInvitations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_NormalizedEmail",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "NormalizedEmail" },
                unique: true,
                filter: "\"StatusCode\" = 'pending'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationInvitations_OrganizationId_NormalizedEmail",
                table: "OrganizationInvitations");
        }
    }
}
