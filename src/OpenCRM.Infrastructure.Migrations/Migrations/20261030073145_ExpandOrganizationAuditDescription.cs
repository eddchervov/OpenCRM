using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ExpandOrganizationAuditDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "OrganizationAuditEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1200)",
                oldMaxLength: 1200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат не должен обрезать уже сохранённую историю изменения прав.
            migrationBuilder.Sql("""
                LOCK TABLE "OrganizationAuditEvents" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationAuditEvents" WHERE char_length("Description") > 1200) THEN
                        RAISE EXCEPTION 'Откат невозможен: аудит содержит описания длиннее 1200 символов.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "OrganizationAuditEvents",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
