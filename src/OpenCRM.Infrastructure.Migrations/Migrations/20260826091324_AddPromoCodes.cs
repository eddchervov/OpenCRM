using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "SubscriptionPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromoCodeId",
                table: "SubscriptionPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoCodeSnapshot",
                table: "SubscriptionPayments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PromoDiscountAmount",
                table: "SubscriptionPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaximumRedemptions = table.Column<int>(type: "integer", nullable: true),
                    DemoDurationDays = table.Column<int>(type: "integer", nullable: true),
                    DiscountTypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountPaymentLimitPerUser = table.Column<int>(type: "integer", nullable: true),
                    AppliesToAllPaidPlans = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodePlans",
                columns: table => new
                {
                    PromoCodeId = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionPlanId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodePlans", x => new { x.PromoCodeId, x.SubscriptionPlanId });
                    table.ForeignKey(
                        name: "FK_PromoCodePlans_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromoCodePlans_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodeRedemptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromoCodeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DemoAppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromoCodeRedemptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromoCodeRedemptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PromoCodeRedemptions_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PromoCodeId",
                table: "SubscriptionPayments",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodePlans_SubscriptionPlanId",
                table: "PromoCodePlans",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemptions_OrganizationId",
                table: "PromoCodeRedemptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemptions_PromoCodeId_UserId",
                table: "PromoCodeRedemptions",
                columns: new[] { "PromoCodeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodeRedemptions_UserId",
                table: "PromoCodeRedemptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_Code",
                table: "PromoCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_IsActive_ValidFrom_ValidUntil",
                table: "PromoCodes",
                columns: new[] { "IsActive", "ValidFrom", "ValidUntil" });

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPayments_PromoCodes_PromoCodeId",
                table: "SubscriptionPayments",
                column: "PromoCodeId",
                principalTable: "PromoCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPayments_PromoCodes_PromoCodeId",
                table: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "PromoCodePlans");

            migrationBuilder.DropTable(
                name: "PromoCodeRedemptions");

            migrationBuilder.DropTable(
                name: "PromoCodes");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_PromoCodeId",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "PromoCodeId",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "PromoCodeSnapshot",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "PromoDiscountAmount",
                table: "SubscriptionPayments");
        }
    }
}
