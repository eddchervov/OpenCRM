using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxNotificationDispatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MaxClientNotifications"
                    ADD COLUMN IF NOT EXISTS "NotificationCommandId" uuid,
                    ADD COLUMN IF NOT EXISTS "NotificationDispatchedAt" timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS "NotificationEventAt" timestamp with time zone,
                    ADD COLUMN IF NOT EXISTS "NotificationId" uuid;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
