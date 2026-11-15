using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddServerAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EngineStateJson = table.Column<string>(type: "jsonb", nullable: false),
                    SelectionJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProcessingToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantConversations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistantConversations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssistantOrganizationAccess",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnabledUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WebEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DailyBudgetRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MonthlyBudgetRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantOrganizationAccess", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_AssistantOrganizationAccess_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SpeechRepliesEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TextModel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SpeechModel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    VoiceModel = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    VoiceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DailyBudgetRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MonthlyBudgetRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    TextReservationRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    AudioReservationRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MaximumOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantSettings", x => x.Id);
                    table.CheckConstraint("CK_AssistantSettings_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "AssistantTelegramLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTelegramLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantTelegramLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantTelegramLinks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantTelegramUpdates",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTelegramUpdates", x => x.UpdateId);
                });

            migrationBuilder.CreateTable(
                name: "AssistantDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CommandJson = table.Column<string>(type: "jsonb", nullable: false),
                    SelectionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Snapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PreviewJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Destructive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistantDrafts_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantDrafts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssistantMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantMessages_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantTelegramBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    SelectedOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpeechRepliesEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTelegramBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantTelegramBindings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantTelegramBindings_AssistantConversations_Conversati~",
                        column: x => x.ConversationId,
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistantTelegramBindings_Organizations_SelectedOrganizatio~",
                        column: x => x.SelectedOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AssistantUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedTokens = table.Column<long>(type: "bigint", nullable: true),
                    AudioSeconds = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    InputCharacters = table.Column<int>(type: "integer", nullable: true),
                    ReservedCostRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    CostRub = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantUsage_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssistantUsage_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssistantUsage_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAudits_CreatedAt",
                table: "AssistantAudits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_OrganizationId",
                table: "AssistantConversations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversations_UserId_OrganizationId_Channel_Expire~",
                table: "AssistantConversations",
                columns: new[] { "UserId", "OrganizationId", "Channel", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantDrafts_ConversationId_Status",
                table: "AssistantDrafts",
                columns: new[] { "ConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantDrafts_OrganizationId",
                table: "AssistantDrafts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantDrafts_UserId",
                table: "AssistantDrafts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_ConversationId_Sequence",
                table: "AssistantMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramBindings_ConversationId",
                table: "AssistantTelegramBindings",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramBindings_SelectedOrganizationId",
                table: "AssistantTelegramBindings",
                column: "SelectedOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramBindings_TelegramUserId",
                table: "AssistantTelegramBindings",
                column: "TelegramUserId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramBindings_UserId",
                table: "AssistantTelegramBindings",
                column: "UserId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramLinks_OrganizationId",
                table: "AssistantTelegramLinks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramLinks_TokenHash",
                table: "AssistantTelegramLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramLinks_UserId",
                table: "AssistantTelegramLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTelegramUpdates_Status_CreatedAt",
                table: "AssistantTelegramUpdates",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsage_ConversationId",
                table: "AssistantUsage",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsage_CreatedAt",
                table: "AssistantUsage",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsage_Model_ProviderRequestId",
                table: "AssistantUsage",
                columns: new[] { "Model", "ProviderRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsage_OrganizationId_CreatedAt",
                table: "AssistantUsage",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantUsage_UserId",
                table: "AssistantUsage",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantAudits");

            migrationBuilder.DropTable(
                name: "AssistantDrafts");

            migrationBuilder.DropTable(
                name: "AssistantMessages");

            migrationBuilder.DropTable(
                name: "AssistantOrganizationAccess");

            migrationBuilder.DropTable(
                name: "AssistantSettings");

            migrationBuilder.DropTable(
                name: "AssistantTelegramBindings");

            migrationBuilder.DropTable(
                name: "AssistantTelegramLinks");

            migrationBuilder.DropTable(
                name: "AssistantTelegramUpdates");

            migrationBuilder.DropTable(
                name: "AssistantUsage");

            migrationBuilder.DropTable(
                name: "AssistantConversations");
        }
    }
}
