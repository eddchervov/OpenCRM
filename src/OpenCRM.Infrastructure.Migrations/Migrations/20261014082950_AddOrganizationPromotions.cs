using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PromoCodeApplicationId",
                table: "SubscriptionPayments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AppliesToAllScenarios",
                table: "PromoCodes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusDays",
                table: "PromoCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiftTemplatePlanId",
                table: "PromoCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrganizationCode",
                table: "PromoCodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaximumOrganizationApplications",
                table: "PromoCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "PaymentCreationOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PromoCodeApplicationId",
                table: "PaymentCreationOperations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromoDaysStartDate",
                table: "OrganizationSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromoCodeApplications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromoCodeId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentCreationOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionPaymentId = table.Column<int>(type: "integer", nullable: true),
                    ApplyActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    BenefitTypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BonusDays = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedSubscriptionId = table.Column<int>(type: "integer", nullable: true),
                    AccessUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeApplications", x => x.Id);
                    table.CheckConstraint("CK_PromoCodeApplications_Status", "\"Status\" IN ('assigned', 'claimed', 'consumed')");
                    table.CheckConstraint("CK_PromoCodeApplications_Timestamps", "(\"Status\" = 'consumed' AND \"ConsumedAt\" IS NOT NULL) OR (\"Status\" <> 'consumed' AND \"ConsumedAt\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PromoCodeApplications_OrganizationSubscriptions_GrantedSubs~",
                        column: x => x.GrantedSubscriptionId,
                        principalTable: "OrganizationSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromoCodeApplications_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromoCodeApplications_PaymentCreationOperations_PaymentCrea~",
                        column: x => x.PaymentCreationOperationId,
                        principalTable: "PaymentCreationOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PromoCodeApplications_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromoCodeApplications_SubscriptionPayments_SubscriptionPaym~",
                        column: x => x.SubscriptionPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodeScenarios",
                columns: table => new
                {
                    PromoCodeId = table.Column<int>(type: "integer", nullable: false),
                    ScenarioCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeScenarios", x => new { x.PromoCodeId, x.ScenarioCode });
                    table.ForeignKey(
                        name: "FK_PromoCodeScenarios_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PromoCodeApplicationId",
                table: "SubscriptionPayments",
                column: "PromoCodeApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_GiftTemplatePlanId",
                table: "PromoCodes",
                column: "GiftTemplatePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_OrganizationId",
                table: "PaymentCreationOperations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_PromoCodeApplicationId",
                table: "PaymentCreationOperations",
                column: "PromoCodeApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_GrantedSubscriptionId",
                table: "PromoCodeApplications",
                column: "GrantedSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_OrganizationId",
                table: "PromoCodeApplications",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_PaymentCreationOperationId",
                table: "PromoCodeApplications",
                column: "PaymentCreationOperationId",
                unique: true,
                filter: "\"PaymentCreationOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_PromoCodeId_OrganizationId",
                table: "PromoCodeApplications",
                columns: new[] { "PromoCodeId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_PromoCodeId_Status",
                table: "PromoCodeApplications",
                columns: new[] { "PromoCodeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeApplications_SubscriptionPaymentId",
                table: "PromoCodeApplications",
                column: "SubscriptionPaymentId",
                unique: true,
                filter: "\"SubscriptionPaymentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentCreationOperations_Organizations_OrganizationId",
                table: "PaymentCreationOperations",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentCreationOperations_PromoCodeApplications_PromoCodeAp~",
                table: "PaymentCreationOperations",
                column: "PromoCodeApplicationId",
                principalTable: "PromoCodeApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PromoCodes_SubscriptionPlans_GiftTemplatePlanId",
                table: "PromoCodes",
                column: "GiftTemplatePlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPayments_PromoCodeApplications_PromoCodeApplica~",
                table: "SubscriptionPayments",
                column: "PromoCodeApplicationId",
                principalTable: "PromoCodeApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Выданные дни и постоянные закрепления нельзя безопасно отменить удалением схемы.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "PromoCodeApplications")
                        OR EXISTS (SELECT 1 FROM "PromoCodes" WHERE "IsOrganizationCode") THEN
                        RAISE EXCEPTION 'Откат невозможен после создания организационных промокодов или их применения. Сначала требуется отдельное восстановление данных.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentCreationOperations_Organizations_OrganizationId",
                table: "PaymentCreationOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_PaymentCreationOperations_PromoCodeApplications_PromoCodeAp~",
                table: "PaymentCreationOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_PromoCodes_SubscriptionPlans_GiftTemplatePlanId",
                table: "PromoCodes");

            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPayments_PromoCodeApplications_PromoCodeApplica~",
                table: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "PromoCodeApplications");

            migrationBuilder.DropTable(
                name: "PromoCodeScenarios");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_PromoCodeApplicationId",
                table: "SubscriptionPayments");

            migrationBuilder.DropIndex(
                name: "IX_PromoCodes_GiftTemplatePlanId",
                table: "PromoCodes");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCreationOperations_OrganizationId",
                table: "PaymentCreationOperations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentCreationOperations_PromoCodeApplicationId",
                table: "PaymentCreationOperations");

            migrationBuilder.DropColumn(
                name: "PromoCodeApplicationId",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "AppliesToAllScenarios",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "BonusDays",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "GiftTemplatePlanId",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "IsOrganizationCode",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "MaximumOrganizationApplications",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "PaymentCreationOperations");

            migrationBuilder.DropColumn(
                name: "PromoCodeApplicationId",
                table: "PaymentCreationOperations");

            migrationBuilder.DropColumn(
                name: "PromoDaysStartDate",
                table: "OrganizationSubscriptions");
        }
    }
}
