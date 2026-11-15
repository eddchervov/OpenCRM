using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Сохраняет все финансовые показатели обслуживания в одной проекции для отчётов. </summary>
    public partial class ExpandMaintenanceFinancialProjection : Migration
    {
        /// <summary> Заполняет проекцию из исходных заказов и работ в общей транзакции. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Maintenance", "MaintenanceDetails", "Vehicles", "Organizations", "OrganizationClients"
                    IN SHARE ROW EXCLUSIVE MODE;
                """);
            migrationBuilder.AlterTable(
                name: "MaintenanceFinancialTotals",
                comment: "Готовые финансовые показатели одного обслуживания. C# обновляет проекцию в транзакции исходного заказа и работ; оплаты, заказ и строки работ остаются источником данных. Финансовые триггеры не используются.",
                oldComment: "Сохранённая сумма работ заказа для ускорения финансовых отчётов и статистики. Контекст C# пересчитывает её в той же транзакции при сохранении строк работ; исходные строки остаются источником данных.");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "MaintenanceFinancialTotals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientName",
                table: "MaintenanceFinancialTotals",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<decimal>(
                name: "DebtAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "MaintenanceFinancialTotals",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "FirstKey",
                table: "MaintenanceFinancialTotals",
                type: "character varying(55)",
                maxLength: 55,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "MaintenanceFinancialTotals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborCostAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialCostAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "MaintenanceFinancialTotals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitAmount",
                table: "MaintenanceFinancialTotals",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UtcDay",
                table: "MaintenanceFinancialTotals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Перестраиваем только производные данные; внешний ключ проверяется целиком после пакетного заполнения.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "OpenCRMFinancialBackfill" ON COMMIT DROP AS
                WITH gross AS (
                    SELECT "MaintenanceId", SUM(COALESCE("Price", 0)) AS amount
                    FROM "MaintenanceDetails" GROUP BY "MaintenanceId"
                ), source AS (
                    SELECT maintenance."Id", COALESCE(gross.amount, 0) AS gross, vehicle."OrganizationId",
                        COALESCE(maintenance."CompletedDate", maintenance."PlannedDate", maintenance."Date") AS effective_date,
                        NOT maintenance."IsDeleted" AND NOT vehicle."IsDeleted" AND vehicle."OrganizationId" IS NOT NULL AS is_visible,
                        maintenance."BuyerClientId" AS client_id,
                        CASE WHEN maintenance."BuyerClientId" IS NOT NULL THEN maintenance."BuyerName" END AS client_name,
                        maintenance."DiscountAmount", maintenance."PaidAmount", maintenance."MaterialCostAmount", maintenance."LaborCostAmount"
                    FROM "Maintenance" AS maintenance
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    LEFT JOIN gross ON gross."MaintenanceId" = maintenance."Id"
                ), calculated AS (
                    SELECT source.*, GREATEST(0, gross - "DiscountAmount") AS net,
                        CASE WHEN effective_date = '-infinity'::timestamptz THEN 0::numeric
                            WHEN effective_date = 'infinity'::timestamptz THEN 315537897599999999::numeric
                            ELSE EXTRACT(EPOCH FROM (effective_date AT TIME ZONE 'UTC' - timestamp '0001-01-01')) * 1000000
                        END AS microseconds
                    FROM source
                )
                SELECT "Id" AS "MaintenanceId", gross AS "GrossAmount", "OrganizationId", effective_date AS "EffectiveDate",
                    FLOOR(microseconds / 86400000000)::integer AS "UtcDay", is_visible AS "IsVisible",
                    client_id AS "ClientId", client_name AS "ClientName", net AS "NetAmount", "PaidAmount",
                    GREATEST(0, net - "PaidAmount") AS "DebtAmount", "DiscountAmount", "MaterialCostAmount", "LaborCostAmount",
                    net - "MaterialCostAmount" - "LaborCostAmount" AS "ProfitAmount",
                    LPAD((315537897599999999::numeric - microseconds)::bigint::text, 18, '0') || ':' || "Id"::text AS "FirstKey"
                FROM calculated ORDER BY "Id";

                ALTER TABLE "MaintenanceFinancialTotals"
                    DROP CONSTRAINT "FK_MaintenanceFinancialTotals_Maintenance_MaintenanceId";
                TRUNCATE TABLE "MaintenanceFinancialTotals";
                INSERT INTO "MaintenanceFinancialTotals"
                    ("MaintenanceId", "GrossAmount", "OrganizationId", "EffectiveDate", "UtcDay", "IsVisible", "ClientId", "ClientName",
                    "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount", "MaterialCostAmount", "LaborCostAmount", "ProfitAmount", "FirstKey")
                SELECT "MaintenanceId", "GrossAmount", "OrganizationId", "EffectiveDate", "UtcDay", "IsVisible", "ClientId", "ClientName",
                    "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount", "MaterialCostAmount", "LaborCostAmount", "ProfitAmount", "FirstKey"
                FROM "OpenCRMFinancialBackfill";
                ALTER TABLE "MaintenanceFinancialTotals"
                    ADD CONSTRAINT "FK_MaintenanceFinancialTotals_Maintenance_MaintenanceId"
                    FOREIGN KEY ("MaintenanceId") REFERENCES "Maintenance" ("Id") ON DELETE CASCADE;
                ANALYZE "MaintenanceFinancialTotals";
                """);
            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceFinancialTotals_DebtDate",
                table: "MaintenanceFinancialTotals",
                columns: new[] { "OrganizationId", "EffectiveDate" },
                filter: "\"IsVisible\" AND \"DebtAmount\" > 0")
                .Annotation("Npgsql:IndexInclude", new[] { "ClientId", "ClientName", "DebtAmount", "FirstKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceFinancialTotals_FinanceDate",
                table: "MaintenanceFinancialTotals",
                columns: new[] { "OrganizationId", "EffectiveDate" },
                filter: "\"IsVisible\"")
                .Annotation("Npgsql:IndexInclude", new[] { "MaintenanceId", "UtcDay", "FirstKey", "GrossAmount", "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount", "MaterialCostAmount", "LaborCostAmount", "ProfitAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceFinancialTotals_OrderDay",
                table: "MaintenanceFinancialTotals",
                columns: new[] { "OrganizationId", "UtcDay", "MaintenanceId" },
                descending: new[] { false, true, true },
                filter: "\"IsVisible\"")
                .Annotation("Npgsql:IndexInclude", new[] { "EffectiveDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceFinancialTotals_Organizations_OrganizationId",
                table: "MaintenanceFinancialTotals",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <summary> Удаляет дополнительные производные поля, сохраняя сумму работ и исходные данные. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceFinancialTotals_Organizations_OrganizationId",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceFinancialTotals_DebtDate",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceFinancialTotals_FinanceDate",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceFinancialTotals_OrderDay",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "ClientName",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "DebtAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "FirstKey",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "LaborCostAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "MaterialCostAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "ProfitAmount",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "UtcDay",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.AlterTable(
                name: "MaintenanceFinancialTotals",
                comment: "Сохранённая сумма работ заказа для ускорения финансовых отчётов и статистики. Контекст C# пересчитывает её в той же транзакции при сохранении строк работ; исходные строки остаются источником данных.",
                oldComment: "Готовые финансовые показатели одного обслуживания. C# обновляет проекцию в транзакции исходного заказа и работ; оплаты, заказ и строки работ остаются источником данных. Финансовые триггеры не используются.");
        }
    }
}
