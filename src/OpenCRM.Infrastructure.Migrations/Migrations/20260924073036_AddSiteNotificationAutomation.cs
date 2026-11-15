using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    public partial class AddSiteNotificationAutomation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "SiteNotifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                table: "SiteNotifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientUserId",
                table: "SiteNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypeCode",
                table: "SiteNotifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.CreateIndex(
                name: "IX_SiteNotifications_EventKey",
                table: "SiteNotifications",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteNotifications_RecipientUserId",
                table: "SiteNotifications",
                column: "RecipientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SiteNotifications_AspNetUsers_RecipientUserId",
                table: "SiteNotifications",
                column: "RecipientUserId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteNotifications_AspNetUsers_RecipientUserId",
                table: "SiteNotifications");

            migrationBuilder.DropIndex(
                name: "IX_SiteNotifications_EventKey",
                table: "SiteNotifications");

            migrationBuilder.DropIndex(
                name: "IX_SiteNotifications_RecipientUserId",
                table: "SiteNotifications");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "SiteNotifications");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                table: "SiteNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "SiteNotifications");

            migrationBuilder.DropColumn(
                name: "TypeCode",
                table: "SiteNotifications");
        }
    }
}
