using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260513023000_AddCarWashScenario")]
    public partial class AddCarWashScenario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "AccountScenarioDefinitions"
                    ("Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES
                    ('car-wash', 'Автомойка', 'Быстрые записи клиентов: дата, время, авто, услуги мойки и стоимость визита.', TRUE, 45, NOW(), NOW())
                ON CONFLICT ("Code") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Description" = EXCLUDED."Description",
                    "IsActive" = EXCLUDED."IsActive",
                    "SortOrder" = EXCLUDED."SortOrder",
                    "UpdatedAt" = NOW();
                """);

            migrationBuilder.Sql("""
                INSERT INTO "AccountScenarioFeatures"
                    ("AccountScenarioDefinitionId", "AccountFeatureDefinitionId", "SortOrder")
                SELECT
                    scenario."Id",
                    feature."Id",
                    scenario_feature."SortOrder"
                FROM (VALUES
                    ('car-wash', 'customer-vehicles', 10),
                    ('car-wash', 'clients', 20),
                    ('car-wash', 'service-history', 30),
                    ('car-wash', 'scheduler', 40),
                    ('car-wash', 'reports', 50)
                ) AS scenario_feature("ScenarioCode", "FeatureCode", "SortOrder")
                JOIN "AccountScenarioDefinitions" scenario ON scenario."Code" = scenario_feature."ScenarioCode"
                JOIN "AccountFeatureDefinitions" feature ON feature."Code" = scenario_feature."FeatureCode"
                ON CONFLICT ("AccountScenarioDefinitionId", "AccountFeatureDefinitionId") DO UPDATE SET
                    "SortOrder" = EXCLUDED."SortOrder";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AccountScenarioFeatures"
                WHERE "AccountScenarioDefinitionId" IN (
                    SELECT "Id"
                    FROM "AccountScenarioDefinitions"
                    WHERE "Code" = 'car-wash'
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM "AccountScenarioDefinitions"
                WHERE "Code" = 'car-wash';
                """);
        }
    }
}
