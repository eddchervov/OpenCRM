using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Отделяет видимость обслуживания от участия в начислениях. </summary>
    public partial class AddMaintenanceAccrualFlag : Migration
    {
        /// <summary> Пересчитывает финансовую проекцию и почасовые услуги с учётом отменённых заказов. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Maintenance", "MaintenanceDetails", "MaintenanceFinancialTotals", "Vehicles", "Organizations",
                    "OrganizationServiceHourlyTotals" IN SHARE ROW EXCLUSIVE MODE;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsAccruable",
                table: "MaintenanceFinancialTotals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                WITH source AS (
                    SELECT maintenance."Id", vehicle."OrganizationId",
                        COALESCE(maintenance."CompletedDate", maintenance."PlannedDate", maintenance."Date") AS effective_date,
                        NOT maintenance."IsDeleted" AND NOT vehicle."IsDeleted" AND vehicle."OrganizationId" IS NOT NULL AS is_visible,
                        maintenance."Status" <> 4 AS is_accruable_status,
                        maintenance."BuyerClientId" AS client_id,
                        CASE WHEN maintenance."BuyerClientId" IS NOT NULL THEN maintenance."BuyerName" END AS client_name,
                        maintenance."DiscountAmount", maintenance."PaidAmount", maintenance."MaterialCostAmount",
                        maintenance."LaborCostAmount", COALESCE(details.amount, 0) AS gross
                    FROM "Maintenance" AS maintenance
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    LEFT JOIN LATERAL (
                        SELECT SUM(COALESCE(detail."Price", 0)) AS amount
                        FROM "MaintenanceDetails" AS detail WHERE detail."MaintenanceId" = maintenance."Id"
                    ) AS details ON TRUE
                ), calculated AS (
                    SELECT source.*, is_visible AND is_accruable_status AS is_accruable,
                        CASE WHEN is_accruable_status THEN GREATEST(0, gross - "DiscountAmount") ELSE 0 END AS net,
                        GREATEST(LEAST(effective_date, TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                            TIMESTAMPTZ '0001-01-01 00:00:00Z') AS normalized_date
                    FROM source
                )
                UPDATE "MaintenanceFinancialTotals" AS total SET
                    "GrossAmount" = calculated.gross,
                    "OrganizationId" = calculated."OrganizationId",
                    "EffectiveDate" = calculated.effective_date,
                    "UtcDay" = FLOOR(EXTRACT(EPOCH FROM calculated.normalized_date) / 86400)::integer + 719162,
                    "IsVisible" = calculated.is_visible,
                    "IsAccruable" = calculated.is_accruable,
                    "ClientId" = calculated.client_id,
                    "ClientName" = calculated.client_name,
                    "NetAmount" = calculated.net,
                    "PaidAmount" = calculated."PaidAmount",
                    "DebtAmount" = CASE WHEN calculated.is_accruable_status
                        THEN GREATEST(0, calculated.net - calculated."PaidAmount") ELSE 0 END,
                    "DiscountAmount" = calculated."DiscountAmount",
                    "MaterialCostAmount" = calculated."MaterialCostAmount",
                    "LaborCostAmount" = calculated."LaborCostAmount",
                    "ProfitAmount" = calculated.net - calculated."MaterialCostAmount" - calculated."LaborCostAmount",
                    "FirstKey" = LPAD((253402300799999999 - (EXTRACT(EPOCH FROM calculated.normalized_date) * 1000000)::bigint)::text, 18, '0')
                        || ':' || calculated."Id"::text
                FROM calculated WHERE total."MaintenanceId" = calculated."Id";

                DELETE FROM "OrganizationServiceHourlyTotals";
                INSERT INTO "OrganizationServiceHourlyTotals"
                    ("OrganizationId", "UtcHour", "NameHash", "Name", "Count", "Amount", "FirstKey")
                SELECT "OrganizationId", "UtcHour", encode(sha256(convert_to("Name", 'UTF8')), 'hex'),
                    "Name", "Count", "Amount", "FirstKey"
                FROM (
                    SELECT total."OrganizationId",
                        FLOOR(EXTRACT(EPOCH FROM GREATEST(
                            LEAST(total."EffectiveDate", TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                            TIMESTAMPTZ '0001-01-01 00:00:00Z')) / 3600)::integer + 17259888 AS "UtcHour",
                        detail."Name" COLLATE "C" AS "Name", COUNT(*) AS "Count", SUM(detail."Price") AS "Amount",
                        MIN((total."FirstKey" || ':' || LPAD(detail."Position"::text, 10, '0')
                            || ':' || detail."DetailId"::text) COLLATE "C") AS "FirstKey"
                    FROM "MaintenanceFinancialTotals" AS total
                    JOIN "MaintenanceDetails" AS detail ON detail."MaintenanceId" = total."MaintenanceId"
                    WHERE total."IsAccruable" AND detail."Price" > 0 AND detail."Name" IS NOT NULL
                    GROUP BY total."OrganizationId", "UtcHour", detail."Name" COLLATE "C"
                ) AS groups;
                ANALYZE "OrganizationServiceHourlyTotals";
                """);

            AddMaintenanceAccrualFlag.RebuildFinancialIndexes(migrationBuilder, true);
        }

        /// <summary> Восстанавливает прежнее начисление и почасовые суммы перед удалением признака. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Maintenance", "MaintenanceDetails", "MaintenanceFinancialTotals", "Vehicles", "Organizations",
                    "OrganizationServiceHourlyTotals" IN SHARE ROW EXCLUSIVE MODE;
                """);

            migrationBuilder.Sql("""
                WITH source AS (
                    SELECT maintenance."Id", vehicle."OrganizationId",
                        COALESCE(maintenance."CompletedDate", maintenance."PlannedDate", maintenance."Date") AS effective_date,
                        NOT maintenance."IsDeleted" AND NOT vehicle."IsDeleted" AND vehicle."OrganizationId" IS NOT NULL AS is_visible,
                        maintenance."BuyerClientId" AS client_id,
                        CASE WHEN maintenance."BuyerClientId" IS NOT NULL THEN maintenance."BuyerName" END AS client_name,
                        maintenance."DiscountAmount", maintenance."PaidAmount", maintenance."MaterialCostAmount",
                        maintenance."LaborCostAmount", COALESCE(details.amount, 0) AS gross
                    FROM "Maintenance" AS maintenance
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    LEFT JOIN LATERAL (
                        SELECT SUM(COALESCE(detail."Price", 0)) AS amount
                        FROM "MaintenanceDetails" AS detail WHERE detail."MaintenanceId" = maintenance."Id"
                    ) AS details ON TRUE
                ), calculated AS (
                    SELECT source.*, GREATEST(0, gross - "DiscountAmount") AS net,
                        GREATEST(LEAST(effective_date, TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                            TIMESTAMPTZ '0001-01-01 00:00:00Z') AS normalized_date
                    FROM source
                )
                UPDATE "MaintenanceFinancialTotals" AS total SET
                    "GrossAmount" = calculated.gross,
                    "OrganizationId" = calculated."OrganizationId",
                    "EffectiveDate" = calculated.effective_date,
                    "UtcDay" = FLOOR(EXTRACT(EPOCH FROM calculated.normalized_date) / 86400)::integer + 719162,
                    "IsVisible" = calculated.is_visible,
                    "ClientId" = calculated.client_id,
                    "ClientName" = calculated.client_name,
                    "NetAmount" = calculated.net,
                    "PaidAmount" = calculated."PaidAmount",
                    "DebtAmount" = GREATEST(0, calculated.net - calculated."PaidAmount"),
                    "DiscountAmount" = calculated."DiscountAmount",
                    "MaterialCostAmount" = calculated."MaterialCostAmount",
                    "LaborCostAmount" = calculated."LaborCostAmount",
                    "ProfitAmount" = calculated.net - calculated."MaterialCostAmount" - calculated."LaborCostAmount",
                    "FirstKey" = LPAD((253402300799999999 - (EXTRACT(EPOCH FROM calculated.normalized_date) * 1000000)::bigint)::text, 18, '0')
                        || ':' || calculated."Id"::text
                FROM calculated WHERE total."MaintenanceId" = calculated."Id";

                DELETE FROM "OrganizationServiceHourlyTotals";
                INSERT INTO "OrganizationServiceHourlyTotals"
                    ("OrganizationId", "UtcHour", "NameHash", "Name", "Count", "Amount", "FirstKey")
                SELECT "OrganizationId", "UtcHour", encode(sha256(convert_to("Name", 'UTF8')), 'hex'),
                    "Name", "Count", "Amount", "FirstKey"
                FROM (
                    SELECT total."OrganizationId",
                        FLOOR(EXTRACT(EPOCH FROM GREATEST(
                            LEAST(total."EffectiveDate", TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                            TIMESTAMPTZ '0001-01-01 00:00:00Z')) / 3600)::integer + 17259888 AS "UtcHour",
                        detail."Name" COLLATE "C" AS "Name", COUNT(*) AS "Count", SUM(detail."Price") AS "Amount",
                        MIN((total."FirstKey" || ':' || LPAD(detail."Position"::text, 10, '0')
                            || ':' || detail."DetailId"::text) COLLATE "C") AS "FirstKey"
                    FROM "MaintenanceFinancialTotals" AS total
                    JOIN "MaintenanceDetails" AS detail ON detail."MaintenanceId" = total."MaintenanceId"
                    WHERE total."IsVisible" AND detail."Price" > 0 AND detail."Name" IS NOT NULL
                    GROUP BY total."OrganizationId", "UtcHour", detail."Name" COLLATE "C"
                ) AS groups;
                ANALYZE "OrganizationServiceHourlyTotals";
                """);

            AddMaintenanceAccrualFlag.RebuildFinancialIndexes(migrationBuilder, false);

            migrationBuilder.DropColumn(
                name: "IsAccruable",
                table: "MaintenanceFinancialTotals");
        }

        /// <summary> Сохраняет покрытие частичных индексов при чтении признака начисления. </summary>
        private static void RebuildFinancialIndexes(MigrationBuilder migrationBuilder, bool includeAccrualFlag)
        {
            string[] financeColumns =
            [
                "MaintenanceId", "UtcDay", "FirstKey", "GrossAmount", "NetAmount", "PaidAmount", "DebtAmount",
                "DiscountAmount", "MaterialCostAmount", "LaborCostAmount", "ProfitAmount"
            ];
            string[] debtColumns = ["ClientId", "ClientName", "DebtAmount", "FirstKey"];
            if (includeAccrualFlag)
            {
                financeColumns = [.. financeColumns, "IsAccruable"];
                debtColumns = [.. debtColumns, "IsAccruable"];
            }

            migrationBuilder.DropIndex("IX_MaintenanceFinancialTotals_FinanceDate", "MaintenanceFinancialTotals");
            migrationBuilder.DropIndex("IX_MaintenanceFinancialTotals_DebtDate", "MaintenanceFinancialTotals");
            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceFinancialTotals_FinanceDate",
                table: "MaintenanceFinancialTotals",
                columns: ["OrganizationId", "EffectiveDate"],
                filter: "\"IsVisible\"")
                .Annotation("Npgsql:IndexInclude", financeColumns);
            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceFinancialTotals_DebtDate",
                table: "MaintenanceFinancialTotals",
                columns: ["OrganizationId", "EffectiveDate"],
                filter: "\"IsVisible\" AND \"DebtAmount\" > 0")
                .Annotation("Npgsql:IndexInclude", debtColumns);
        }
    }
}
