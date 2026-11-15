using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Ускоряет фильтрацию и постраничное чтение журнала обслуживания по его эффективной дате. </summary>
    public partial class IndexMaintenanceJournalDates : Migration
    {
        /// <summary> Создаёт индекс выражений в порядке строк журнала без длительной блокировки записи. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Удаление остатка позволяет повторить миграцию после прерванного конкурентного построения.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Journal_EffectiveDate_Order\";",
                suppressTransaction: true);

            // Исходные поля CASE нужны планировщику для подсчёта по индексу без чтения всех строк таблицы.
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Journal_EffectiveDate_Order"
                ON "Maintenance" (
                    (CASE WHEN "Status" = 3 THEN COALESCE("CompletedDate", "Date")
                        ELSE COALESCE("PlannedDate", "Date") END) DESC NULLS FIRST,
                    (CASE WHEN "Status" = 3 THEN NULL::interval
                        ELSE "PlannedStartTime" END) DESC NULLS FIRST,
                    "DateTimeCreation" DESC,
                    "Id" DESC
                ) INCLUDE ("VehicleId", "Status", "CompletedDate", "Date", "PlannedDate")
                WHERE NOT "IsDeleted";
                """,
                suppressTransaction: true);
        }

        /// <summary> Удаляет только индекс журнала, сохраняя предметные данные и остальные индексы. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Journal_EffectiveDate_Order\";",
                suppressTransaction: true);
        }
    }
}
