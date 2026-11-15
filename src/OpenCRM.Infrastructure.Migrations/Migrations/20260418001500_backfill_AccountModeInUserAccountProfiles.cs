using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260418001500_backfill_AccountModeInUserAccountProfiles")]
    public partial class backfill_AccountModeInUserAccountProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "UserAccountProfiles"
                SET "AccountMode" = CASE
                    WHEN lower(coalesce(nullif(trim("PrimaryScenarioCode"), ''), 'personal')) = 'personal' THEN 'personal'
                    ELSE 'organization'
                END
                WHERE coalesce("AccountMode", '') = '';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "UserAccountProfiles"
                SET "AccountMode" = ''
                WHERE "AccountMode" IN ('personal', 'organization');
                """);
        }
    }
}
