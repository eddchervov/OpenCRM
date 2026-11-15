using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentAdminActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPaymentAdminActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    RefundedBeforeMinor = table.Column<long>(type: "bigint", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StateToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProviderCheckRequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPaymentAdminActions", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPaymentAdminActions_Amount", "\"AmountMinor\" IS NULL OR \"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPaymentAdminActions_SubscriptionPurchases_Purch~",
                        column: x => x.PurchaseId,
                        principalTable: "SubscriptionPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentAdminActions_CompletedAt_NextAttemptAt",
                table: "SubscriptionPaymentAdminActions",
                columns: new[] { "CompletedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentAdminActions_PurchaseId_CreatedAt",
                table: "SubscriptionPaymentAdminActions",
                columns: new[] { "PurchaseId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Административный финансовый журнал нельзя удалить автоматическим откатом после начала работы.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SubscriptionPaymentAdminActions") THEN
                        RAISE EXCEPTION 'Нельзя удалить журнал административных платёжных операций с сохранёнными данными.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "SubscriptionPaymentAdminActions");
        }
    }
}
