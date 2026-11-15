using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationChannelsAndMessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImmutableProviderBotId",
                table: "OrganizationMaxBots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "TelegramChatId",
                table: "OrganizationConversations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "BotId",
                table: "OrganizationConversations",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                table: "OrganizationConversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "OrganizationConversations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalConversationId",
                table: "OrganizationConversations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MessageTemplateId",
                table: "OrganizationConversationMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MessageTemplateVersion",
                table: "OrganizationConversationMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderUpdateId",
                table: "OrganizationConversationMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderExternalUserId",
                table: "OrganizationConversationMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextReconcileAt",
                table: "OrganizationConversationDispatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicationReplayCount",
                table: "OrganizationConversationDispatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrganizationClients_OrganizationId_Id",
                table: "OrganizationClients",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "MessageTemplateGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTemplateGroups", x => x.Id);
                    table.UniqueConstraint("AK_MessageTemplateGroups_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_MessageTemplateGroups_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationChannelConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ImmutableProviderIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationChannelConnections", x => x.Id);
                    table.UniqueConstraint("AK_OrganizationChannelConnections_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_OrganizationChannelConnections_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                WITH identities AS (
                    SELECT "OrganizationId", "BotId" AS provider_bot_id
                    FROM "OrganizationConversations"
                    WHERE "BotId" IS NOT NULL
                    UNION
                    SELECT "OrganizationId", "TelegramBotId" AS provider_bot_id
                    FROM "OrganizationTelegramBots"
                    WHERE "TelegramBotId" IS NOT NULL
                )
                INSERT INTO "OrganizationChannelConnections" (
                    "Id", "OrganizationId", "ProviderCode", "ImmutableProviderIdentity", "Username",
                    "IsCurrent", "VerifiedAt", "RetiredAt", "CreatedAt")
                SELECT
                    gen_random_uuid(),
                    identities."OrganizationId",
                    'telegram',
                    identities.provider_bot_id::text,
                    COALESCE(bot."Username", (
                        SELECT conversation."BotUsername"
                        FROM "OrganizationConversations" conversation
                        WHERE conversation."OrganizationId" = identities."OrganizationId"
                            AND conversation."BotId" = identities.provider_bot_id
                        ORDER BY conversation."UpdatedAt" DESC
                        LIMIT 1
                    ), ''),
                    COALESCE(bot."IsEnabled", FALSE),
                    COALESCE(bot."UpdatedAt", (
                        SELECT MIN(conversation."CreatedAt")
                        FROM "OrganizationConversations" conversation
                        WHERE conversation."OrganizationId" = identities."OrganizationId"
                            AND conversation."BotId" = identities.provider_bot_id
                    ), now()),
                    CASE WHEN COALESCE(bot."IsEnabled", FALSE) THEN NULL ELSE now() END,
                    COALESCE(bot."UpdatedAt", (
                        SELECT MIN(conversation."CreatedAt")
                        FROM "OrganizationConversations" conversation
                        WHERE conversation."OrganizationId" = identities."OrganizationId"
                            AND conversation."BotId" = identities.provider_bot_id
                    ), now())
                FROM identities
                LEFT JOIN "OrganizationTelegramBots" bot
                    ON bot."OrganizationId" = identities."OrganizationId"
                    AND bot."TelegramBotId" = identities.provider_bot_id;

                UPDATE "OrganizationConversations" conversation
                SET "ConnectionId" = connection."Id",
                    "ExternalConversationId" = conversation."TelegramChatId"::text,
                    "DeliveryAddress" = conversation."TelegramChatId"::text
                FROM "OrganizationChannelConnections" connection
                WHERE connection."OrganizationId" = conversation."OrganizationId"
                    AND connection."ProviderCode" = 'telegram'
                    AND connection."ImmutableProviderIdentity" = conversation."BotId"::text;

                UPDATE "OrganizationConversationMessages"
                SET "ProviderUpdateId" = "TelegramUpdateId"::text,
                    "SenderExternalUserId" = "SenderTelegramUserId"::text
                WHERE "TelegramUpdateId" IS NOT NULL OR "SenderTelegramUserId" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "MessageTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageTemplates_MessageTemplateGroups_OrganizationId_Group~",
                        columns: x => new { x.OrganizationId, x.GroupId },
                        principalTable: "MessageTemplateGroups",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationClientChannelLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExternalConversationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VerificationSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationClientChannelLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationClientChannelLinks_OrganizationChannelConnectio~",
                        columns: x => new { x.OrganizationId, x.ConnectionId },
                        principalTable: "OrganizationChannelConnections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationClientChannelLinks_OrganizationClients_Organiza~",
                        columns: x => new { x.OrganizationId, x.ClientId },
                        principalTable: "OrganizationClients",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "OrganizationClientChannelLinks" (
                    "Id", "OrganizationId", "ClientId", "ConnectionId", "ExternalUserId",
                    "ExternalConversationId", "VerificationSource", "VerifiedAt", "IsActive")
                SELECT
                    gen_random_uuid(),
                    client."OrganizationId",
                    client."Id",
                    connection."Id",
                    client."TelegramChatId"::text,
                    client."TelegramChatId"::text,
                    'legacy_client',
                    client."TelegramConnectedAt",
                    TRUE
                FROM "OrganizationClients" client
                JOIN "OrganizationChannelConnections" connection
                    ON connection."OrganizationId" = client."OrganizationId"
                    AND connection."ProviderCode" = 'telegram'
                    AND connection."IsCurrent" = TRUE
                WHERE client."TelegramChatId" IS NOT NULL
                    AND client."TelegramConnectedAt" IS NOT NULL
                    AND client."IsDeleted" = FALSE;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_ConnectionId_ExternalConversation~",
                table: "OrganizationConversations",
                columns: new[] { "ConnectionId", "ExternalConversationId" },
                unique: true,
                filter: "\"ConnectionId\" IS NOT NULL AND \"ExternalConversationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_OrganizationId_ConnectionId",
                table: "OrganizationConversations",
                columns: new[] { "OrganizationId", "ConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_ProviderUpd~",
                table: "OrganizationConversationMessages",
                columns: new[] { "ConversationId", "ProviderUpdateId" },
                unique: true,
                filter: "\"ProviderUpdateId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_MessageTemplateId",
                table: "OrganizationConversationMessages",
                column: "MessageTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationDispatches_NextReconcileAt",
                table: "OrganizationConversationDispatches",
                column: "NextReconcileAt",
                filter: "\"NextReconcileAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageTemplateGroups_OrganizationId_SortOrder_Id",
                table: "MessageTemplateGroups",
                columns: new[] { "OrganizationId", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageTemplates_OrganizationId_GroupId_IsArchived_Name",
                table: "MessageTemplates",
                columns: new[] { "OrganizationId", "GroupId", "IsArchived", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationChannelConnections_OrganizationId_ProviderCode_~",
                table: "OrganizationChannelConnections",
                columns: new[] { "OrganizationId", "ProviderCode", "ImmutableProviderIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationChannelConnections_OrganizationId_ProviderCode~1",
                table: "OrganizationChannelConnections",
                columns: new[] { "OrganizationId", "ProviderCode", "IsCurrent" },
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientChannelLinks_ConnectionId_ExternalConvers~",
                table: "OrganizationClientChannelLinks",
                columns: new[] { "ConnectionId", "ExternalConversationId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientChannelLinks_OrganizationId_ClientId_IsAc~",
                table: "OrganizationClientChannelLinks",
                columns: new[] { "OrganizationId", "ClientId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationClientChannelLinks_OrganizationId_ConnectionId",
                table: "OrganizationClientChannelLinks",
                columns: new[] { "OrganizationId", "ConnectionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationConversationMessages_MessageTemplates_MessageTe~",
                table: "OrganizationConversationMessages",
                column: "MessageTemplateId",
                principalTable: "MessageTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationConversations_OrganizationChannelConnections_Or~",
                table: "OrganizationConversations",
                columns: new[] { "OrganizationId", "ConnectionId" },
                principalTable: "OrganizationChannelConnections",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "OrganizationConversations"
                        WHERE "BotId" IS NULL OR "TelegramChatId" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Нельзя откатить общие каналы: существуют диалоги без Telegram-идентификаторов.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationConversationMessages_MessageTemplates_MessageTe~",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationConversations_OrganizationChannelConnections_Or~",
                table: "OrganizationConversations");

            migrationBuilder.DropTable(
                name: "MessageTemplates");

            migrationBuilder.DropTable(
                name: "OrganizationClientChannelLinks");

            migrationBuilder.DropTable(
                name: "MessageTemplateGroups");

            migrationBuilder.DropTable(
                name: "OrganizationChannelConnections");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationConversations_ConnectionId_ExternalConversation~",
                table: "OrganizationConversations");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationConversations_OrganizationId_ConnectionId",
                table: "OrganizationConversations");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_ProviderUpd~",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationConversationMessages_MessageTemplateId",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationConversationDispatches_NextReconcileAt",
                table: "OrganizationConversationDispatches");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrganizationClients_OrganizationId_Id",
                table: "OrganizationClients");

            migrationBuilder.DropColumn(
                name: "ImmutableProviderBotId",
                table: "OrganizationMaxBots");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "OrganizationConversations");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "OrganizationConversations");

            migrationBuilder.DropColumn(
                name: "ExternalConversationId",
                table: "OrganizationConversations");

            migrationBuilder.DropColumn(
                name: "MessageTemplateId",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropColumn(
                name: "MessageTemplateVersion",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropColumn(
                name: "ProviderUpdateId",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropColumn(
                name: "SenderExternalUserId",
                table: "OrganizationConversationMessages");

            migrationBuilder.DropColumn(
                name: "NextReconcileAt",
                table: "OrganizationConversationDispatches");

            migrationBuilder.DropColumn(
                name: "PublicationReplayCount",
                table: "OrganizationConversationDispatches");

            migrationBuilder.AlterColumn<long>(
                name: "TelegramChatId",
                table: "OrganizationConversations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "BotId",
                table: "OrganizationConversations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
