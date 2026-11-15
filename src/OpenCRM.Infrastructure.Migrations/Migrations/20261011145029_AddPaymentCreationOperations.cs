using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCreationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "SubscriptionPayments" IN SHARE ROW EXCLUSIVE MODE;
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT "ExternalPaymentId" FROM "SubscriptionPayments"
                        WHERE "ExternalPaymentId" IS NOT NULL
                        GROUP BY "ExternalPaymentId" HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'В SubscriptionPayments найдены дубли ExternalPaymentId. Проверьте историю платежей до применения миграции.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.CreateTable(
                name: "PaymentCreationOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotenceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SubscriptionPlanId = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromoDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PromoCodeId = table.Column<int>(type: "integer", nullable: true),
                    PromoCodeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ReturnUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BuyerTypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BuyerInn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    RequestTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestBody = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionPaymentId = table.Column<int>(type: "integer", nullable: true),
                    ExternalPaymentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaymentLink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ProviderCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCreationOperations", x => x.Id);
                    table.CheckConstraint("CK_PaymentCreationOperations_Lease", "(\"Status\" = 'sending' AND \"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'sending' AND \"LeaseToken\" IS NULL AND \"LeaseUntil\" IS NULL)");
                    table.CheckConstraint("CK_PaymentCreationOperations_Status", "\"Status\" IN ('prepared', 'sending', 'created', 'rejected', 'manual_review')");
                    table.ForeignKey(
                        name: "FK_PaymentCreationOperations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCreationOperations_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PaymentCreationOperations_SubscriptionPayments_Subscription~",
                        column: x => x.SubscriptionPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCreationOperations_SubscriptionPlans_SubscriptionPla~",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_ExternalPaymentId",
                table: "SubscriptionPayments",
                column: "ExternalPaymentId",
                unique: true,
                filter: "\"ExternalPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_ExternalPaymentId",
                table: "PaymentCreationOperations",
                column: "ExternalPaymentId",
                unique: true,
                filter: "\"ExternalPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_IdempotenceKey",
                table: "PaymentCreationOperations",
                column: "IdempotenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_PromoCodeId",
                table: "PaymentCreationOperations",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_Status_LeaseUntil",
                table: "PaymentCreationOperations",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_SubscriptionPaymentId",
                table: "PaymentCreationOperations",
                column: "SubscriptionPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_SubscriptionPlanId",
                table: "PaymentCreationOperations",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCreationOperations_UserId",
                table: "PaymentCreationOperations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "PaymentCreationOperations" IN ACCESS EXCLUSIVE MODE;
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "PaymentCreationOperations") THEN
                        RAISE EXCEPTION 'Откат запрещён: сохранены операции создания оплаты. Удаление ключей может привести к повторной оплате.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.DropTable(
                name: "PaymentCreationOperations");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_ExternalPaymentId",
                table: "SubscriptionPayments");
        }
    }
}
