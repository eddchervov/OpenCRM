using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Хранит цены услуг и согласованных работ в точном денежном типе. </summary>
    public partial class UseDecimalMaintenancePrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                LOCK TABLE "MaintenanceDetails", "MaintenanceWorkReports" IN ACCESS EXCLUSIVE MODE;
                DO $money$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "MaintenanceDetails"
                        WHERE "Price" IS NOT NULL AND (
                            "Price"::text IN ('NaN', 'Infinity', '-Infinity')
                            OR abs("Price") >= 10000000000000000
                            OR "Price"::numeric <> round("Price"::numeric, 2)))
                        OR EXISTS (
                        SELECT 1 FROM "MaintenanceWorkReports"
                        WHERE "ApprovedPrice" IS NOT NULL AND (
                            "ApprovedPrice"::text IN ('NaN', 'Infinity', '-Infinity')
                            OR abs("ApprovedPrice") >= 10000000000000000
                            OR "ApprovedPrice"::numeric <> round("ApprovedPrice"::numeric, 2)))
                    THEN
                        RAISE EXCEPTION 'Цены содержат недопустимые значения или доли копейки. Исправьте данные до миграции.';
                    END IF;
                END $money$;
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "ApprovedPrice",
                table: "MaintenanceWorkReports",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "MaintenanceDetails",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                LOCK TABLE "MaintenanceDetails", "MaintenanceWorkReports" IN ACCESS EXCLUSIVE MODE;
                DO $money$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "MaintenanceDetails"
                        WHERE "Price" <> ("Price"::double precision)::numeric)
                        OR EXISTS (SELECT 1 FROM "MaintenanceWorkReports"
                        WHERE "ApprovedPrice" <> ("ApprovedPrice"::double precision)::numeric)
                    THEN
                        RAISE EXCEPTION 'Откат потеряет точность новых цен. Экспортируйте и обработайте эти значения перед откатом.';
                    END IF;
                END $money$;
                """);
            migrationBuilder.AlterColumn<double>(
                name: "ApprovedPrice",
                table: "MaintenanceWorkReports",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Price",
                table: "MaintenanceDetails",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
