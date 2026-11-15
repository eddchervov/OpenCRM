using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationQueuedAt",
                table: "SubscriptionExpiryReminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignRecipientId",
                table: "EmailMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextDispatchAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificationAttemptCount",
                table: "EmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "NotificationCommandId",
                table: "EmailMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationDispatchedAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotificationEventAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NotificationId",
                table: "EmailMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationKey",
                table: "EmailMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationStatus",
                table: "EmailMessages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SubscriptionReminderId",
                table: "EmailMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateDataJson",
                table: "EmailMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateKey",
                table: "EmailMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemplateRegisteredAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SimulatedCount",
                table: "EmailCampaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_NextDispatchAt",
                table: "EmailMessages",
                column: "NextDispatchAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_NotificationCommandId",
                table: "EmailMessages",
                column: "NotificationCommandId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_NotificationKey",
                table: "EmailMessages",
                column: "NotificationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TemplateKey",
                table: "EmailMessages",
                column: "TemplateKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат не должен молча удалять ещё не обработанные задания.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "EmailMessages" WHERE "NextDispatchAt" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Нельзя откатить миграцию при наличии необработанных уведомлений.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_NextDispatchAt",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_NotificationCommandId",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_NotificationKey",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_TemplateKey",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationQueuedAt",
                table: "SubscriptionExpiryReminders");

            migrationBuilder.DropColumn(
                name: "CampaignRecipientId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NextDispatchAt",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationAttemptCount",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationCommandId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationDispatchedAt",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationEventAt",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationKey",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "NotificationStatus",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "SubscriptionReminderId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "TemplateDataJson",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "TemplateKey",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "TemplateRegisteredAt",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "SimulatedCount",
                table: "EmailCampaigns");
        }
    }
}
