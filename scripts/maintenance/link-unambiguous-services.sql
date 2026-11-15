-- Выполнять после аудита и резервного копирования базы, при остановленной записи данных приложением.
-- Связывает только строки без ID с единственной одноимённой услугой той же организации.
-- Названия, цены, порядок строк и остальные поля сохраняются. Повторный запуск безопасен.
BEGIN;
LOCK TABLE "Maintenance", "MaintenanceDetails", "OrganizationScenarioCatalogItems" IN SHARE ROW EXCLUSIVE MODE;
WITH service_lines AS (
    SELECT d."MaintenanceId" AS maintenance_id, d."DetailId" AS detail_id,
           matches.candidate_id
    FROM "MaintenanceDetails" d
    JOIN "Maintenance" m ON m."Id" = d."MaintenanceId"
    JOIN "Vehicles" v ON v."Id" = m."VehicleId"
    CROSS JOIN LATERAL (
        SELECT count(*) AS match_count, min(c."Id"::text) AS candidate_id
        FROM "OrganizationScenarioCatalogItems" c
        WHERE c."OrganizationId" = v."OrganizationId" AND c."KindCode" = 'service'
          AND lower(btrim(c."Name")) = lower(btrim(d."Name"))
    ) matches
    WHERE v."OrganizationId" IS NOT NULL
      AND d."CatalogItemId" IS NULL
      AND d."MaterialCatalogItemId" IS NULL
      AND NULLIF(btrim(d."MaterialName"), '') IS NULL
      AND d."MaterialQuantity" IS NULL
      AND matches.match_count = 1
), updated_details AS (
    UPDATE "MaintenanceDetails" d SET "CatalogItemId" = service_lines.candidate_id::uuid
    FROM service_lines
    WHERE d."MaintenanceId" = service_lines.maintenance_id
      AND d."DetailId" = service_lines.detail_id
    RETURNING d."MaintenanceId"
), repaired AS (
    SELECT DISTINCT "MaintenanceId" FROM updated_details
), versioned AS (
    UPDATE "Maintenance" m SET "Comment" = m."Comment"
    FROM repaired
    WHERE m."Id" = repaired."MaintenanceId"
    RETURNING m."Id"
)
SELECT "Id" AS repaired_maintenance_id FROM versioned;
COMMIT;
