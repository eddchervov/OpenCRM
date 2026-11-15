using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "SupportMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportMessageEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupportMessageId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IsStaff = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessageEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportMessageEntries_SupportMessages_SupportMessageId",
                        column: x => x.SupportMessageId,
                        principalTable: "SupportMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_OrganizationId",
                table: "SupportMessages",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessageEntries_SupportMessageId_CreatedAt",
                table: "SupportMessageEntries",
                columns: new[] { "SupportMessageId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportMessages_Organizations_OrganizationId",
                table: "SupportMessages",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportMessages_Organizations_OrganizationId",
                table: "SupportMessages");

            migrationBuilder.DropTable(
                name: "SupportMessageEntries");

            migrationBuilder.DropIndex(
                name: "IX_SupportMessages_OrganizationId",
                table: "SupportMessages");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "SupportMessages");
        }
    }
}
