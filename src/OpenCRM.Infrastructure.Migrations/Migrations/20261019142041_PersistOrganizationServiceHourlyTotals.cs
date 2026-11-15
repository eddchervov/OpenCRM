using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Создаёт восстанавливаемую почасовую сводку услуг для финансового рейтинга. </summary>
    public partial class PersistOrganizationServiceHourlyTotals : Migration
    {
        /// <summary> Заполняет сводку в транзакции, сохраняя исходные заказы и работы. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Maintenance", "MaintenanceDetails", "MaintenanceFinancialTotals", "Vehicles", "Organizations"
                    IN SHARE ROW EXCLUSIVE MODE;
                """);

            migrationBuilder.CreateTable(
                name: "OrganizationServiceHourlyTotals",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtcHour = table.Column<int>(type: "integer", nullable: false),
                    NameHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    FirstKey = table.Column<string>(type: "character varying(103)", maxLength: 103, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationServiceHourlyTotals", x => new { x.OrganizationId, x.UtcHour, x.NameHash });
                    table.CheckConstraint("CK_OrganizationServiceHourlyTotals_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_OrganizationServiceHourlyTotals_Count", "\"Count\" > 0");
                    table.CheckConstraint("CK_OrganizationServiceHourlyTotals_NameHash", "\"NameHash\" = encode(sha256(convert_to(\"Name\", 'UTF8')), 'hex')");
                    table.CheckConstraint("CK_OrganizationServiceHourlyTotals_UtcHour", "\"UtcHour\" BETWEEN 0 AND 87649415");
                    table.ForeignKey(
                        name: "FK_OrganizationServiceHourlyTotals_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Производная сводка положительных строк услуг по организации, часу UTC и исходному имени. C# обновляет её в транзакции обслуживания без триггеров; источник данных — заказы и строки работ.");

            // Группировка исходного имени предшествует хешированию: разные имена никогда не объединяются по хешу.
            migrationBuilder.Sql("""
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
        }

        /// <summary> Удаляет только производную сводку; исходные данные сохраняются. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationServiceHourlyTotals");
        }
    }
}
