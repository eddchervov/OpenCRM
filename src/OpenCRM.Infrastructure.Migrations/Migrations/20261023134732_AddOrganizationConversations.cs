using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramBotId",
                table: "OrganizationTelegramBots",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<long>(type: "bigint", nullable: false),
                    BotUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramDisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TelegramUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OrganizationClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastMessageText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationConversations_OrganizationClients_OrganizationC~",
                        column: x => x.OrganizationClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationConversations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ActorDisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderTelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    TelegramUpdateId = table.Column<long>(type: "bigint", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SafeToRetry = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationMessages_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationMessages_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationMessages_OrganizationConversations_~",
                        column: x => x.ConversationId,
                        principalTable: "OrganizationConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationConversationReadStates",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    LastReadSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConversationReadStates", x => new { x.ConversationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_OrganizationConversationReadStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationReadStates_OrganizationConversation~",
                        column: x => x.ConversationId,
                        principalTable: "OrganizationConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationConversationAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ProviderFileId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ProviderFileUniqueId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StorageReference = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConversationAttachments", x => x.Id);
                    table.CheckConstraint("CK_OrganizationConversationAttachments_SizeBytes", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_OrganizationConversationAttachments_AspNetUsers_UploadedByU~",
                        column: x => x.UploadedByUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationAttachments_OrganizationConversatio~",
                        column: x => x.ConversationId,
                        principalTable: "OrganizationConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationAttachments_OrganizationConversati~1",
                        column: x => x.MessageId,
                        principalTable: "OrganizationConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationAttachments_Organizations_Organizat~",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationConversationDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationConversationDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationConversationDispatches_OrganizationConversation~",
                        column: x => x.MessageId,
                        principalTable: "OrganizationConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationTelegramBots_TelegramBotId",
                table: "OrganizationTelegramBots",
                column: "TelegramBotId",
                unique: true,
                filter: "\"TelegramBotId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationAttachments_ConversationId_MessageI~",
                table: "OrganizationConversationAttachments",
                columns: new[] { "ConversationId", "MessageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationAttachments_MessageId_SortOrder",
                table: "OrganizationConversationAttachments",
                columns: new[] { "MessageId", "SortOrder" },
                unique: true,
                filter: "\"MessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationAttachments_OrganizationId_Uploaded~",
                table: "OrganizationConversationAttachments",
                columns: new[] { "OrganizationId", "UploadedByUserId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationAttachments_ProviderFileUniqueId",
                table: "OrganizationConversationAttachments",
                column: "ProviderFileUniqueId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationAttachments_UploadedByUserId",
                table: "OrganizationConversationAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationDispatches_MessageId",
                table: "OrganizationConversationDispatches",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationDispatches_PublishedAt_LeaseExpires~",
                table: "OrganizationConversationDispatches",
                columns: new[] { "PublishedAt", "LeaseExpiresAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ActorUserId_RequestId",
                table: "OrganizationConversationMessages",
                columns: new[] { "ActorUserId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_Direction_S~",
                table: "OrganizationConversationMessages",
                columns: new[] { "ConversationId", "Direction", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_ProviderMes~",
                table: "OrganizationConversationMessages",
                columns: new[] { "ConversationId", "ProviderMessageId" },
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_Sequence",
                table: "OrganizationConversationMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_ConversationId_TelegramUpd~",
                table: "OrganizationConversationMessages",
                columns: new[] { "ConversationId", "TelegramUpdateId" },
                unique: true,
                filter: "\"TelegramUpdateId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_MaintenanceId",
                table: "OrganizationConversationMessages",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_Status_LeaseExpiresAt_Crea~",
                table: "OrganizationConversationMessages",
                columns: new[] { "Status", "LeaseExpiresAt", "CreatedAt" },
                filter: "\"Direction\" = 'outgoing' AND \"Status\" IN ('queued', 'sending')");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationMessages_Status_RetryAt_CreatedAt",
                table: "OrganizationConversationMessages",
                columns: new[] { "Status", "RetryAt", "CreatedAt" },
                filter: "\"Direction\" = 'outgoing' AND \"Status\" IN ('queued', 'failed')");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversationReadStates_UserId_LastReadSequence",
                table: "OrganizationConversationReadStates",
                columns: new[] { "UserId", "LastReadSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_OrganizationClientId",
                table: "OrganizationConversations",
                column: "OrganizationClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_OrganizationId_BotId_TelegramChat~",
                table: "OrganizationConversations",
                columns: new[] { "OrganizationId", "BotId", "TelegramChatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_OrganizationId_LastMessageAt_Id",
                table: "OrganizationConversations",
                columns: new[] { "OrganizationId", "LastMessageAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationConversations_OrganizationId_OrganizationClient~",
                table: "OrganizationConversations",
                columns: new[] { "OrganizationId", "OrganizationClientId", "LastMessageAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationConversationAttachments");

            migrationBuilder.DropTable(
                name: "OrganizationConversationDispatches");

            migrationBuilder.DropTable(
                name: "OrganizationConversationReadStates");

            migrationBuilder.DropTable(
                name: "OrganizationConversationMessages");

            migrationBuilder.DropTable(
                name: "OrganizationConversations");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationTelegramBots_TelegramBotId",
                table: "OrganizationTelegramBots");

            migrationBuilder.DropColumn(
                name: "TelegramBotId",
                table: "OrganizationTelegramBots");
        }
    }
}
