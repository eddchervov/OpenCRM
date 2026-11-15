using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class SeparateAppliedDiscountValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountValidityDays",
                table: "PromoCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiscountValidUntil",
                table: "PromoCodeApplications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Сохранённые сроки скидок нельзя удалить без изменения обещанных условий.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "PromoCodes" WHERE "DiscountValidityDays" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM "PromoCodeApplications" WHERE "DiscountValidUntil" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Откат невозможен при сохранённых сроках использования скидок. Требуется отдельное восстановление условий.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "DiscountValidityDays",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "DiscountValidUntil",
                table: "PromoCodeApplications");
        }
    }
}
