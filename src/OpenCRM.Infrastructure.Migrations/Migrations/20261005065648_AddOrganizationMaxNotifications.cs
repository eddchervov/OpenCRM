using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMaxNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MaxConnectedAt",
                table: "OrganizationClients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MaxLinkExpiresAt",
                table: "OrganizationClients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaxLinkTokenHash",
                table: "OrganizationClients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MaxNotificationsEnabled",
                table: "OrganizationClients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "MaxUserId",
                table: "OrganizationClients",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaxClientNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceVersion = table.Column<long>(type: "bigint", nullable: false),
                    MaxUserId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_MaxClientNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMaxBots",
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
                    table.PrimaryKey("PK_OrganizationMaxBots", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_OrganizationMaxBots_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_MaxLinkTokenHash",
                table: "OrganizationClients",
                column: "MaxLinkTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClients_OrganizationId_MaxUserId",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "MaxUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaxClientNotifications_NotificationKey",
                table: "MaxClientNotifications",
                column: "NotificationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxClientNotifications_Status_NextAttemptAt",
                table: "MaxClientNotifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaxBots_Username",
                table: "OrganizationMaxBots",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxClientNotifications");

            migrationBuilder.DropTable(
                name: "OrganizationMaxBots");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_MaxLinkTokenHash",
                table: "OrganizationClients");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationClients_OrganizationId_MaxUserId",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MaxConnectedAt",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MaxLinkExpiresAt",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MaxLinkTokenHash",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MaxNotificationsEnabled",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "MaxUserId",
                table: "OrganizationClients");
        }
    }
}
