using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelProviderConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientChannelLinks_ConnectionId_ExternalUserId",
                table: "OrganizationClientChannelLinks",
                columns: new[] { "ConnectionId", "ExternalUserId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChannelConnections_ProviderCode",
                table: "OrganizationChannelConnections",
                sql: "\"ProviderCode\" IN ('telegram', 'max')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationClientChannelLinks_ConnectionId_ExternalUserId",
                table: "OrganizationClientChannelLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChannelConnections_ProviderCode",
                table: "OrganizationChannelConnections");
        }
    }
}
