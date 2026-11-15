using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationTelegramNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramConnectedAt",
                table: "OrganizationClients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramLinkExpiresAt",
                table: "OrganizationClients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLinkTokenHash",
                table: "OrganizationClients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelegramNotificationsEnabled",
                table: "OrganizationClients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "OrganizationClients",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TelegramClientNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    EventCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NotificationKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NotificationCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationEventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramClientNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationTelegramBots",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProtectedToken = table.Column<string>(type: "text", nullable: false),
                    ProtectedWebhookSecret = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyBooking = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyReady = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationTelegramBots", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_OrganizationTelegramBots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_TelegramLinkTokenHash",
                table: "OrganizationClients",
                column: "TelegramLinkTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId_TelegramChatId",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "TelegramChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramClientNotifications_NotificationKey",
                table: "TelegramClientNotifications",
                column: "NotificationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramClientNotifications_Status_NextAttemptAt",
                table: "TelegramClientNotifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTelegramBots_Username",
                table: "OrganizationTelegramBots",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramClientNotifications");

            migrationBuilder.DropTable(
                name: "OrganizationTelegramBots");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_TelegramLinkTokenHash",
                table: "OrganizationClients");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_OrganizationId_TelegramChatId",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "TelegramConnectedAt",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "TelegramLinkExpiresAt",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "TelegramLinkTokenHash",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "TelegramNotificationsEnabled",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "OrganizationClients");
        }
    }
}
