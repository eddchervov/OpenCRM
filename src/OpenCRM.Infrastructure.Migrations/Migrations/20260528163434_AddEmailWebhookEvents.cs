using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClickCount",
                table: "EmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEventAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEventCode",
                table: "EmailMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenCount",
                table: "EmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "EmailMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "EmailMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderResponse",
                table: "EmailMessages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailMessageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailMessageId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CampaignId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryInfo = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    SignatureVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EventAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailMessageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailMessageEvents_EmailMessages_EmailMessageId",
                        column: x => x.EmailMessageId,
                        principalTable: "EmailMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_LastEventAt",
                table: "EmailMessages",
                column: "LastEventAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_ProviderMessageId",
                table: "EmailMessages",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_Email_EventAt",
                table: "EmailMessageEvents",
                columns: new[] { "Email", "EventAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_EmailMessageId",
                table: "EmailMessageEvents",
                column: "EmailMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_EventAt",
                table: "EmailMessageEvents",
                column: "EventAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_Provider_ProviderMessageId",
                table: "EmailMessageEvents",
                columns: new[] { "Provider", "ProviderMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_ReceivedAt",
                table: "EmailMessageEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_Status_EventAt",
                table: "EmailMessageEvents",
                columns: new[] { "Status", "EventAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailMessageEvents");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_LastEventAt",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_ProviderMessageId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "ClickCount",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "LastEventAt",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "LastEventCode",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "OpenCount",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "ProviderResponse",
                table: "EmailMessages");
        }
    }
}
