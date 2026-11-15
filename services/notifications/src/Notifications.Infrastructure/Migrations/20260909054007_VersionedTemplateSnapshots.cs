using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VersionedTemplateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationTemplates",
                schema: "notifications",
                table: "NotificationTemplates");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "notifications",
                table: "NotificationTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "IsHtml",
                schema: "notifications",
                table: "Notifications",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                schema: "notifications",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationTemplates",
                schema: "notifications",
                table: "NotificationTemplates",
                columns: new[] { "ApplicationId", "Key", "Channel", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Прежняя схема не может сохранить несколько версий одного шаблона.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM notifications."NotificationTemplates" WHERE "Version" <> 1)
                       OR EXISTS (SELECT 1 FROM notifications."Notifications" WHERE "TemplateVersion" > 1) THEN
                        RAISE EXCEPTION 'Cannot downgrade: template versions other than 1 are in use.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationTemplates",
                schema: "notifications",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "notifications",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "IsHtml",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationTemplates",
                schema: "notifications",
                table: "NotificationTemplates",
                columns: new[] { "ApplicationId", "Key", "Channel" });
        }
    }
}
