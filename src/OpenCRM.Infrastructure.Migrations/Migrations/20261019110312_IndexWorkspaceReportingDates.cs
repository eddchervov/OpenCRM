using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Ускоряет выборку периодов финансовой сводки и статистики. </summary>
    public partial class IndexWorkspaceReportingDates : Migration
    {
        /// <summary> Создаёт индексы дат отчётов без длительной блокировки обычной записи. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Остаток прерванного конкурентного построения должен допускать повтор миграции.
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Finance_Date\";", suppressTransaction: true);
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Finance_Date"
                ON "Maintenance" ((COALESCE("CompletedDate", "PlannedDate", "Date")) DESC, "Id")
                INCLUDE ("VehicleId", "CompletedDate", "PlannedDate", "Date", "BuyerClientId", "BuyerName",
                    "DiscountAmount", "PaidAmount", "MaterialCostAmount", "LaborCostAmount")
                WHERE NOT "IsDeleted";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Analytics_CompletedDate\";", suppressTransaction: true);
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Analytics_CompletedDate"
                ON "Maintenance" ((COALESCE("CompletedDate", "Date")))
                INCLUDE ("Id", "VehicleId", "CompletedDate", "Date", "PaidAmount", "DiscountAmount")
                WHERE "Status" = 3 AND NOT "IsDeleted";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Analytics_CreatedDate\";", suppressTransaction: true);
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "IX_Maintenance_Analytics_CreatedDate"
                ON "Maintenance" ("DateTimeCreation")
                INCLUDE ("VehicleId")
                WHERE NOT "IsDeleted";
                """,
                suppressTransaction: true);
            // Статистика частичного индекса выражения не оценивает селективность периода для планировщика.
            migrationBuilder.Sql(
                """
                DROP STATISTICS IF EXISTS "ST_Maintenance_Finance_Date";
                CREATE STATISTICS "ST_Maintenance_Finance_Date"
                    ON (COALESCE("CompletedDate", "PlannedDate", "Date")) FROM "Maintenance";
                DROP STATISTICS IF EXISTS "ST_Maintenance_Analytics_CompletedDate";
                CREATE STATISTICS "ST_Maintenance_Analytics_CompletedDate"
                    ON (COALESCE("CompletedDate", "Date")) FROM "Maintenance";
                ANALYZE "Maintenance";
                """);
        }

        /// <summary> Удаляет только индексы отчётов, сохраняя предметные данные. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP STATISTICS IF EXISTS \"ST_Maintenance_Analytics_CompletedDate\"; DROP STATISTICS IF EXISTS \"ST_Maintenance_Finance_Date\";");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Analytics_CreatedDate\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Analytics_CompletedDate\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Maintenance_Finance_Date\";", suppressTransaction: true);
        }
    }
}
