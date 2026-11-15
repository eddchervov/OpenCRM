using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Ускоряет выборку текущих работ и выдач рабочего дня. </summary>
    public partial class IndexWorkdayActiveOrders : Migration
    {
        /// <summary> Создаёт частичные индексы без блокировки обычных записей на время построения. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Индексы выражений поддерживаются SQL отдельно от модели, как индексы производных назначений.
            // Предварительное удаление позволяет повторить миграцию после частичного сбоя или невалидного построения.
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_Planned\";", suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Workday_Planned"
                ON "Maintenance" ((COALESCE("PlannedDate", "Date")) DESC)
                INCLUDE ("VehicleId")
                WHERE "Status" = 1 AND NOT "IsDeleted";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_InProgress\";", suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Workday_InProgress"
                ON "Maintenance" ((COALESCE("PlannedDate", "Date")) DESC)
                INCLUDE ("VehicleId")
                WHERE "Status" = 2 AND NOT "IsDeleted";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_Ready\";", suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Workday_Ready"
                ON "Maintenance" ("ReadyForHandoverAt" DESC)
                INCLUDE ("VehicleId")
                WHERE "Status" = 3 AND "ReadyForHandoverAt" IS NOT NULL
                    AND "HandedOverAt" IS NULL AND NOT "IsDeleted";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_HandedOver\";", suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Workday_HandedOver"
                ON "Maintenance" ("HandedOverAt" DESC)
                INCLUDE ("VehicleId")
                WHERE "Status" = 3 AND "HandedOverAt" IS NOT NULL AND NOT "IsDeleted";
                """,
                suppressTransaction: true);
        }

        /// <summary> Удаляет только добавленные индексы, сохраняя все предметные данные. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_HandedOver\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_Ready\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_InProgress\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Workday_Planned\";", suppressTransaction: true);
        }
    }
}
