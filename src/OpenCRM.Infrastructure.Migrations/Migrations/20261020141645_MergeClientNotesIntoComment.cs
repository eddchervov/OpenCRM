using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Объединяет заметки клиента с комментарием без обрезки сохранённого текста. </summary>
    public partial class MergeClientNotesIntoComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OrganizationClients",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(600)",
                oldMaxLength: 600,
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "OrganizationClients"
                SET "Comment" = CASE
                    WHEN "Notes" IS NULL OR BTRIM("Notes") = '' THEN "Comment"
                    WHEN "Comment" IS NULL OR BTRIM("Comment") = '' THEN "Notes"
                    WHEN "Comment" = "Notes" THEN "Comment"
                    ELSE "Comment" || E'\n\n' || "Notes"
                END;
                """);

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "OrganizationClients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Исходное разделение двух текстов после объединения однозначно не восстанавливается.
            migrationBuilder.Sql("""
                LOCK TABLE "OrganizationClients" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationClients" WHERE "Comment" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Автоматический откат объединённых комментариев невозможен. Восстановите исходные комментарии и заметки из резервной копии.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "OrganizationClients",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "OrganizationClients",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
