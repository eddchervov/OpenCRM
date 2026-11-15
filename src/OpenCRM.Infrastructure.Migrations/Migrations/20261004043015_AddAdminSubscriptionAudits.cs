using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSubscriptionAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceCode",
                table: "UserSubscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "SourceCode",
                table: "UserSubscriptionHistory",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "SourceCode",
                table: "OrganizationSubscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.CreateTable(
                name: "SubscriptionAudits",
                schema: "admin",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAudits_Scope_ClientId_ChangedAt",
                schema: "admin",
                table: "SubscriptionAudits",
                columns: new[] { "Scope", "ClientId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAudits_Scope_SubscriptionId_ChangedAt",
                schema: "admin",
                table: "SubscriptionAudits",
                columns: new[] { "Scope", "SubscriptionId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionAudits",
                schema: "admin");

            migrationBuilder.DropColumn(
                name: "SourceCode",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "SourceCode",
                table: "UserSubscriptionHistory");

            migrationBuilder.DropColumn(
                name: "SourceCode",
                table: "OrganizationSubscriptions");
        }
    }
}
