using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeServiceBundlesAndMaintenanceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrganizationScenarioCatalogItems_Id_OrganizationId_KindCode",
                table: "OrganizationScenarioCatalogItems",
                columns: new[] { "Id", "OrganizationId", "KindCode" });

            migrationBuilder.CreateTable(
                name: "MaintenanceDetails",
                columns: table => new
                {
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Price = table.Column<double>(type: "double precision", nullable: true),
                    VatRateCode = table.Column<string>(type: "text", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeName = table.Column<string>(type: "text", nullable: true),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    MaterialCatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterialName = table.Column<string>(type: "text", nullable: true),
                    MaterialQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    MaterialUnit = table.Column<string>(type: "text", nullable: true),
                    MaterialUnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    MaterialTotalCost = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceDetails", x => new { x.MaintenanceId, x.DetailId });
                    table.CheckConstraint("CK_MaintenanceDetails_Position", "\"Position\" >= 0");
                    table.ForeignKey(
                        name: "FK_MaintenanceDetails_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationServiceBundleItems",
                columns: table => new
                {
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleKindCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ServiceKindCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationServiceBundleItems", x => new { x.BundleId, x.ServiceId });
                    table.CheckConstraint("CK_OrganizationServiceBundleItems_KindAndPosition", "\"BundleKindCode\" = 'service_bundle' AND \"ServiceKindCode\" = 'service' AND \"Position\" >= 0");
                    table.ForeignKey(
                        name: "FK_OrganizationServiceBundleItems_OrganizationScenarioCatalogI~",
                        columns: x => new { x.BundleId, x.OrganizationId, x.BundleKindCode },
                        principalTable: "OrganizationScenarioCatalogItems",
                        principalColumns: new[] { "Id", "OrganizationId", "KindCode" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationServiceBundleItems_OrganizationScenarioCatalog~1",
                        columns: x => new { x.ServiceId, x.OrganizationId, x.ServiceKindCode },
                        principalTable: "OrganizationScenarioCatalogItems",
                        principalColumns: new[] { "Id", "OrganizationId", "KindCode" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceDetails_MaintenanceId_Position",
                table: "MaintenanceDetails",
                columns: new[] { "MaintenanceId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceBundleItems_BundleId_OrganizationId_Bund~",
                table: "OrganizationServiceBundleItems",
                columns: new[] { "BundleId", "OrganizationId", "BundleKindCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceBundleItems_BundleId_Position",
                table: "OrganizationServiceBundleItems",
                columns: new[] { "BundleId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceBundleItems_ServiceId",
                table: "OrganizationServiceBundleItems",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationServiceBundleItems_ServiceId_OrganizationId_Ser~",
                table: "OrganizationServiceBundleItems",
                columns: new[] { "ServiceId", "OrganizationId", "ServiceKindCode" });

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "OrganizationScenarioCatalogItems" b
                        WHERE (b."KindCode" = 'service_bundle' AND cardinality(b."ServiceIds") = 0)
                           OR cardinality(b."ServiceIds") > 100 OR array_ndims(b."ServiceIds") > 1
                           OR (b."KindCode" <> 'service_bundle' AND cardinality(b."ServiceIds") > 0)
                           OR cardinality(b."ServiceIds") <> (SELECT count(DISTINCT id) FROM unnest(b."ServiceIds") id)
                    ) THEN
                        RAISE EXCEPTION 'Некорректный состав набора: пустой набор, повторяющаяся услуга или состав у другого типа записи. Исправьте данные до миграции.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "OrganizationScenarioCatalogItems" b
                        CROSS JOIN LATERAL unnest(b."ServiceIds") ids(id)
                        LEFT JOIN "OrganizationScenarioCatalogItems" s ON s."Id" = ids.id
                        WHERE s."Id" IS NULL OR s."OrganizationId" <> b."OrganizationId" OR s."KindCode" <> 'service'
                    ) THEN
                        RAISE EXCEPTION 'Некорректная ссылка в наборе: отсутствующая услуга, другая организация или неверный тип. Исправьте данные до миграции.';
                    END IF;
                END $$;

                INSERT INTO "OrganizationServiceBundleItems" ("BundleId", "ServiceId", "OrganizationId", "BundleKindCode", "ServiceKindCode", "Position")
                SELECT b."Id", ids.id, b."OrganizationId", 'service_bundle', 'service', (ids.position - 1)::integer
                FROM "OrganizationScenarioCatalogItems" b
                CROSS JOIN LATERAL unnest(b."ServiceIds") WITH ORDINALITY ids(id, position);

                ALTER TABLE "OrganizationServiceBundleItems"
                ADD CONSTRAINT "UQ_OrganizationServiceBundleItems_Position" UNIQUE ("BundleId", "Position") DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Maintenance" m WHERE m."Details" IS NULL
                        OR jsonb_typeof(m."Details"::jsonb) <> 'array') THEN
                        RAISE EXCEPTION 'Строки заказа должны быть JSON-массивом. Исправьте данные до миграции.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Maintenance" m
                        CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(m."Details"::jsonb) = 'array' THEN m."Details"::jsonb ELSE '[]'::jsonb END) d
                        WHERE jsonb_typeof(d) <> 'object' OR d->>'Name' IS NULL
                        OR d->>'DetailId' IS NULL OR (d->>'DetailId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    ) THEN
                        RAISE EXCEPTION 'Некорректная строка заказа, отсутствующее название или пустой DetailId. Исправьте данные до миграции, сохранив связи с операциями.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Maintenance" m
                        CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(m."Details"::jsonb) = 'array' THEN m."Details"::jsonb ELSE '[]'::jsonb END) d
                        CROSS JOIN LATERAL jsonb_object_keys(d) field
                        WHERE field NOT IN ('DetailId', 'Name', 'Description', 'Price', 'VatRateCode', 'EmployeeId', 'EmployeeName', 'CatalogItemId', 'Quantity', 'MaterialCatalogItemId', 'MaterialName', 'MaterialQuantity', 'MaterialUnit', 'MaterialUnitCost', 'MaterialTotalCost')
                    ) THEN
                        RAISE EXCEPTION 'В JSON строк заказа есть неизвестные поля. Уточните схему переноса: удаление этих данных не допускается.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Maintenance" m
                        CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(m."Details"::jsonb) = 'array' THEN m."Details"::jsonb ELSE '[]'::jsonb END) d
                        GROUP BY m."Id", (d->>'DetailId')::uuid
                        HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'В заказе повторяется DetailId, включая пустые идентификаторы. Исправьте данные до миграции, сохранив ссылки на строки.';
                    END IF;
                END $$;

                INSERT INTO "MaintenanceDetails" ("MaintenanceId", "Position", "DetailId", "Name", "Description", "Price", "VatRateCode", "EmployeeId", "EmployeeName", "CatalogItemId", "Quantity", "MaterialCatalogItemId", "MaterialName", "MaterialQuantity", "MaterialUnit", "MaterialUnitCost", "MaterialTotalCost")
                SELECT m."Id", (line.position - 1)::integer, (line.detail->>'DetailId')::uuid,
                    (line.detail->>'Name')::text,
                    (line.detail->>'Description')::text,
                    (line.detail->>'Price')::double precision,
                    (line.detail->>'VatRateCode')::text,
                    (line.detail->>'EmployeeId')::uuid,
                    (line.detail->>'EmployeeName')::text,
                    (line.detail->>'CatalogItemId')::uuid,
                    COALESCE((line.detail->>'Quantity')::integer, 1),
                    (line.detail->>'MaterialCatalogItemId')::uuid,
                    (line.detail->>'MaterialName')::text,
                    (line.detail->>'MaterialQuantity')::numeric,
                    (line.detail->>'MaterialUnit')::text,
                    (line.detail->>'MaterialUnitCost')::numeric,
                    (line.detail->>'MaterialTotalCost')::numeric
                FROM "Maintenance" m
                CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(m."Details"::jsonb) = 'array' THEN m."Details"::jsonb ELSE '[]'::jsonb END)
                    WITH ORDINALITY line(detail, position);

                ALTER TABLE "MaintenanceDetails"
                ADD CONSTRAINT "UQ_MaintenanceDetails_Position" UNIQUE ("MaintenanceId", "Position") DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF (SELECT count(*) FROM "OrganizationServiceBundleItems") <>
                        (SELECT COALESCE(sum(cardinality("ServiceIds")), 0) FROM "OrganizationScenarioCatalogItems")
                    OR (SELECT count(*) FROM "MaintenanceDetails") <>
                        (SELECT COALESCE(sum(CASE WHEN jsonb_typeof("Details"::jsonb) = 'array'
                            THEN jsonb_array_length("Details"::jsonb) ELSE 0 END), 0) FROM "Maintenance") THEN
                        RAISE EXCEPTION 'Количество перенесенных связей или строк заказа не совпадает с исходными данными.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "ServiceIds",
                table: "OrganizationScenarioCatalogItems");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "Maintenance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "ServiceIds",
                table: "OrganizationScenarioCatalogItems",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "Maintenance",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "OrganizationScenarioCatalogItems" b
                SET "ServiceIds" = COALESCE((
                    SELECT array_agg(items."ServiceId" ORDER BY items."Position")
                    FROM "OrganizationServiceBundleItems" items WHERE items."BundleId" = b."Id"
                ), ARRAY[]::uuid[]);
                """);

            migrationBuilder.Sql("""
                UPDATE "Maintenance" m SET "Details" = COALESCE((
                    SELECT jsonb_agg(jsonb_build_object(
                        'DetailId', line."DetailId",
                        'Name', line."Name",
                        'Description', line."Description",
                        'Price', line."Price",
                        'VatRateCode', line."VatRateCode",
                        'EmployeeId', line."EmployeeId",
                        'EmployeeName', line."EmployeeName",
                        'CatalogItemId', line."CatalogItemId",
                        'Quantity', line."Quantity",
                        'MaterialCatalogItemId', line."MaterialCatalogItemId",
                        'MaterialName', line."MaterialName",
                        'MaterialQuantity', line."MaterialQuantity",
                        'MaterialUnit', line."MaterialUnit",
                        'MaterialUnitCost', line."MaterialUnitCost",
                        'MaterialTotalCost', line."MaterialTotalCost"
                    ) ORDER BY line."Position") FROM "MaintenanceDetails" line WHERE line."MaintenanceId" = m."Id"
                ), '[]'::jsonb)::text;
                """);

            migrationBuilder.DropTable(
                name: "MaintenanceDetails");

            migrationBuilder.DropTable(
                name: "OrganizationServiceBundleItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrganizationScenarioCatalogItems_Id_OrganizationId_KindCode",
                table: "OrganizationScenarioCatalogItems");
        }
    }
}
