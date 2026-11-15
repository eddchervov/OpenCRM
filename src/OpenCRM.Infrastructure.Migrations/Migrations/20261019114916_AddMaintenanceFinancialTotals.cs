using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

/// <summary> Хранит согласованные финансовые итоги строк обслуживания. </summary>
public partial class AddMaintenanceFinancialTotals : Migration
{
    /// <summary> Создаёт производные итоги для последующего пересчёта контекстом C# в транзакции сохранения строк. </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_MaintenanceDetails_MaintenanceId_Position\";", suppressTransaction: true);
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY "IX_MaintenanceDetails_MaintenanceId_Position"
            ON "MaintenanceDetails" ("MaintenanceId", "Position") INCLUDE ("Price", "DetailId");
            """, suppressTransaction: true);
        migrationBuilder.Sql(
            """
            LOCK TABLE "MaintenanceDetails" IN SHARE ROW EXCLUSIVE MODE;
            CREATE TABLE "MaintenanceFinancialTotals" (
                "MaintenanceId" uuid NOT NULL CONSTRAINT "PK_MaintenanceFinancialTotals" PRIMARY KEY,
                "GrossAmount" numeric(28,2) NOT NULL);
            COMMENT ON TABLE "MaintenanceFinancialTotals" IS 'Сохранённая сумма работ заказа для ускорения финансовых отчётов и статистики. Контекст C# пересчитывает её в той же транзакции при сохранении строк работ; исходные строки остаются источником данных.';
            INSERT INTO "MaintenanceFinancialTotals" ("MaintenanceId", "GrossAmount")
            SELECT "MaintenanceId", SUM(COALESCE("Price", 0)) FROM "MaintenanceDetails" GROUP BY "MaintenanceId";
            ALTER TABLE "MaintenanceFinancialTotals" ADD CONSTRAINT "FK_MaintenanceFinancialTotals_Maintenance_MaintenanceId"
                FOREIGN KEY ("MaintenanceId") REFERENCES "Maintenance" ("Id") ON DELETE CASCADE;

            ANALYZE "MaintenanceFinancialTotals";
            """);
    }

    /// <summary> Удаляет только производные данные и возвращает прежний индекс строк. </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "MaintenanceFinancialTotals";
            """);
        migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_MaintenanceDetails_MaintenanceId_Position\";", suppressTransaction: true);
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY "IX_MaintenanceDetails_MaintenanceId_Position"
            ON "MaintenanceDetails" ("MaintenanceId", "Position");
            """, suppressTransaction: true);
    }
}
