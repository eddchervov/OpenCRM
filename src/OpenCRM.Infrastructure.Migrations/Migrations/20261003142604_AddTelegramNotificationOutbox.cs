using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    NotificationStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NotificationCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NotificationEventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextDispatchAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramNotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotificationOutbox_NextDispatchAt",
                table: "TelegramNotificationOutbox",
                column: "NextDispatchAt");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotificationOutbox_NotificationCommandId",
                table: "TelegramNotificationOutbox",
                column: "NotificationCommandId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramNotificationOutbox_NotificationKey",
                table: "TelegramNotificationOutbox",
                column: "NotificationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramNotificationOutbox");
        }
    }
}
