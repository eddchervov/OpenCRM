using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class add_SubscriptionPlanTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlanTypeCode",
                table: "SubscriptionPlans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "personal");

            migrationBuilder.Sql("""
                UPDATE "SubscriptionPlans"
                SET "PlanTypeCode" = 'personal'
                WHERE "PlanTypeCode" IS NULL OR "PlanTypeCode" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_PlanTypeCode",
                table: "SubscriptionPlans",
                column: "PlanTypeCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPlans_PlanTypeCode",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PlanTypeCode",
                table: "SubscriptionPlans");
        }
    }
}
