using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VisitStartsAtUtc",
                table: "TelegramClientNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitReminderChannelCode",
                table: "Organizations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<int>(
                name: "VisitReminderHoursBefore",
                table: "Organizations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitReminderTemplate",
                table: "Organizations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisitStartsAtUtc",
                table: "MaxClientNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisitReminderEnabled",
                table: "Maintenance",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "TelegramClientNotifications" WHERE "EventCode" = 'booking_reminder'
                        AND "Status" IN ('queued', 'retrying', 'processing'))
                        OR EXISTS (SELECT 1 FROM "MaxClientNotifications" WHERE "EventCode" = 'booking_reminder'
                        AND "Status" IN ('queued', 'retrying', 'processing')) THEN
                        RAISE EXCEPTION 'Сначала завершите или остановите ожидающие напоминания о визите.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "VisitStartsAtUtc",
                table: "TelegramClientNotifications");

            migrationBuilder.DropColumn(
                name: "VisitReminderChannelCode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VisitReminderHoursBefore",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VisitReminderTemplate",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "VisitStartsAtUtc",
                table: "MaxClientNotifications");

            migrationBuilder.DropColumn(
                name: "VisitReminderEnabled",
                table: "Maintenance");
        }
    }
}
