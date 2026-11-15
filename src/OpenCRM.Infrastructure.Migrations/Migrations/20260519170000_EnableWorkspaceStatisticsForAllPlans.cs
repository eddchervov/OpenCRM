using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260519170000_EnableWorkspaceStatisticsForAllPlans")]
    public partial class EnableWorkspaceStatisticsForAllPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "FeatureDefinitions"
                SET "Name" = 'Статистика',
                    "Description" = 'Открывает отдельный раздел статистики, графики, отчеты и сводные показатели рабочего пространства.'
                WHERE "Code" = 'workspace.module.analytics';
                """);

            migrationBuilder.Sql("""
                INSERT INTO "SubscriptionPlanFeatures"
                    ("SubscriptionPlanId", "FeatureDefinitionId", "BoolValue", "IntValue")
                SELECT
                    plan."Id",
                    feature."Id",
                    TRUE,
                    NULL
                FROM "SubscriptionPlans" plan
                JOIN "FeatureDefinitions" feature ON feature."Code" = 'workspace.module.analytics'
                WHERE plan."PlanTypeCode" = 'business'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "SubscriptionPlanFeatures" existing
                      WHERE existing."SubscriptionPlanId" = plan."Id"
                        AND existing."FeatureDefinitionId" = feature."Id"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "FeatureDefinitions"
                SET "Name" = 'Аналитика',
                    "Description" = 'Открывает KPI, графики, выручку и сводные показатели рабочего пространства.'
                WHERE "Code" = 'workspace.module.analytics';
                """);
        }
    }
}
