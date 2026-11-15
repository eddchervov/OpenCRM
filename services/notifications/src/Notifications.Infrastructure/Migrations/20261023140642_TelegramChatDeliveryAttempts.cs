using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramChatDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramChatDeliveryStates",
                schema: "notifications",
                columns: table => new
                {
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SafeToRetry = table.Column<bool>(type: "boolean", nullable: false),
                    RetryAfterAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChatDeliveryStates", x => x.LeaseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatDeliveryStates_MessageId",
                schema: "notifications",
                table: "TelegramChatDeliveryStates",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatDeliveryStates_RequestId",
                schema: "notifications",
                table: "TelegramChatDeliveryStates",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatDeliveryStates_UpdatedAt",
                schema: "notifications",
                table: "TelegramChatDeliveryStates",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramChatDeliveryStates",
                schema: "notifications");
        }
    }
}
