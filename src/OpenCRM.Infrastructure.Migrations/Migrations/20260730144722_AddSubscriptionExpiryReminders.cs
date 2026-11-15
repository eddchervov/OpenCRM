using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionExpiryReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionExpiryReminders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScopeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EventCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionExpiryReminders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExpiryReminders_CreatedAt",
                table: "SubscriptionExpiryReminders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExpiryReminders_ScopeCode_SubscriptionId_Subscr~",
                table: "SubscriptionExpiryReminders",
                columns: new[] { "ScopeCode", "SubscriptionId", "SubscriptionEndDate", "EventCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionExpiryReminders_SentAt",
                table: "SubscriptionExpiryReminders",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionExpiryReminders");
        }
    }
}
