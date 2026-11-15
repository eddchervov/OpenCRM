using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class LimitOrganizationScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AccountScenarioDefinitions"
                SET "IsActive" = FALSE,
                    "UpdatedAt" = NOW()
                WHERE "Code" IN ('wrapping', 'car-wash');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "AccountScenarioDefinitions"
                SET "IsActive" = TRUE,
                    "UpdatedAt" = NOW()
                WHERE "Code" IN ('wrapping', 'car-wash');
                """);
        }
    }
}
