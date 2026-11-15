using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260429224500_ExtendSupportMessages")]
    public partial class ExtendSupportMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Topic",
                table: "SupportMessages",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "SupportMessages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "AdminComment",
                table: "SupportMessages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "SupportMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "SupportMessages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "SupportMessages",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpHash",
                table: "SupportMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SupportMessages",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "SupportMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourcePage",
                table: "SupportMessages",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusCode",
                table: "SupportMessages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "new");

            migrationBuilder.AddColumn<string>(
                name: "TypeCode",
                table: "SupportMessages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "question");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SupportMessages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "SupportMessages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "SupportMessages",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_Email",
                table: "SupportMessages",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_IpHash",
                table: "SupportMessages",
                column: "IpHash");

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_StatusCode_CreatedAt",
                table: "SupportMessages",
                columns: new[] { "StatusCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_UserId",
                table: "SupportMessages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SupportMessages_Email", table: "SupportMessages");
            migrationBuilder.DropIndex(name: "IX_SupportMessages_IpHash", table: "SupportMessages");
            migrationBuilder.DropIndex(name: "IX_SupportMessages_StatusCode_CreatedAt", table: "SupportMessages");
            migrationBuilder.DropIndex(name: "IX_SupportMessages_UserId", table: "SupportMessages");

            migrationBuilder.DropColumn(name: "AdminComment", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "ClosedAt", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "Email", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "IpHash", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "Name", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "Phone", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "SourcePage", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "StatusCode", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "TypeCode", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "UserAgent", table: "SupportMessages");
            migrationBuilder.DropColumn(name: "UserId", table: "SupportMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Topic",
                table: "SupportMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(180)",
                oldMaxLength: 180);

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "SupportMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);
        }
    }
}
