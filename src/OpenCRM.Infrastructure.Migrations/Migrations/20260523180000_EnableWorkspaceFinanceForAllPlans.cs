using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260523180000_EnableWorkspaceFinanceForAllPlans")]
    public partial class EnableWorkspaceFinanceForAllPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "FeatureDefinitions"
                    ("Code", "Name", "Description", "Category", "ScopeCode", "AudienceCode", "ValueType", "IsActive", "IsSystem")
                SELECT
                    'workspace.module.finance',
                    'Финансы',
                    'Финансовая сводка: выручка, долги, себестоимость, прибыль, графики и заказы за период.',
                    'Модули',
                    'organization',
                    'user',
                    1,
                    TRUE,
                    TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "FeatureDefinitions"
                    WHERE "Code" = 'workspace.module.finance'
                );
                """);

            migrationBuilder.Sql("""
                UPDATE "FeatureDefinitions"
                SET "Name" = 'Финансы',
                    "Description" = 'Финансовая сводка: выручка, долги, себестоимость, прибыль, графики и заказы за период.',
                    "Category" = 'Модули',
                    "ScopeCode" = 'organization',
                    "AudienceCode" = 'user',
                    "ValueType" = 1,
                    "IsActive" = TRUE,
                    "IsSystem" = TRUE
                WHERE "Code" = 'workspace.module.finance';
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
                JOIN "FeatureDefinitions" feature ON feature."Code" = 'workspace.module.finance'
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
                DELETE FROM "SubscriptionPlanFeatures"
                WHERE "FeatureDefinitionId" IN (
                    SELECT "Id"
                    FROM "FeatureDefinitions"
                    WHERE "Code" = 'workspace.module.finance'
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM "FeatureDefinitions"
                WHERE "Code" = 'workspace.module.finance';
                """);
        }
    }
}
