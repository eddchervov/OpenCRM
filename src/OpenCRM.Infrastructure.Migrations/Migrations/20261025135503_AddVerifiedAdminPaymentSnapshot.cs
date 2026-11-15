using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedAdminPaymentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerifiedStateToken",
                table: "SubscriptionPaymentAdminActions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Сохранённые результаты финансовых проверок остаются частью административного журнала.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SubscriptionPaymentAdminActions") THEN
                        RAISE EXCEPTION 'Нельзя удалить журнал административных платёжных операций с сохранёнными данными.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "VerifiedStateToken",
                table: "SubscriptionPaymentAdminActions");
        }
    }
}
