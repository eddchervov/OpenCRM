using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CompleteNotificationIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemplateRegisteredAt",
                table: "EmailMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "EmailMessages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AddColumn<Guid>(
                name: "NotificationEventId",
                table: "EmailMessageEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessageEvents_NotificationEventId",
                table: "EmailMessageEvents",
                column: "NotificationEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailMessageEvents_NotificationEventId",
                table: "EmailMessageEvents");

            migrationBuilder.DropColumn(
                name: "NotificationEventId",
                table: "EmailMessageEvents");

            migrationBuilder.AlterColumn<string>(
                name: "Subject",
                table: "EmailMessages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemplateRegisteredAt",
                table: "EmailMessages",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
