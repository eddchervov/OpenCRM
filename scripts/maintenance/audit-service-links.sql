-- Проверка связей услуг во всех организациях. Скрипт только читает данные.
WITH service_lines AS (
    SELECT m."Id" AS maintenance_id, v."OrganizationId" AS organization_id,
           d."Position" + 1 AS line_number, d."Name" AS saved_name,
           d."CatalogItemId"::text AS catalog_id
    FROM "MaintenanceDetails" d
    JOIN "Maintenance" m ON m."Id" = d."MaintenanceId"
    JOIN "Vehicles" v ON v."Id" = m."VehicleId"
    WHERE v."OrganizationId" IS NOT NULL
      AND d."MaterialCatalogItemId" IS NULL
      AND NULLIF(btrim(d."MaterialName"), '') IS NULL
      AND d."MaterialQuantity" IS NULL
), candidates AS (
    SELECT l.*, matches.match_count, matches.candidate_id, linked."IsActive" AS linked_active,
           CASE
               WHEN linked."Id" IS NOT NULL THEN CASE WHEN linked."IsActive" THEN 'linked' ELSE 'archived' END
               WHEN l.catalog_id IS NOT NULL THEN 'invalid_link'
               WHEN matches.match_count = 1 THEN 'can_link'
               WHEN matches.match_count > 1 THEN 'ambiguous'
               ELSE 'not_found'
           END AS status
    FROM service_lines l
    LEFT JOIN "OrganizationScenarioCatalogItems" linked ON linked."Id"::text = lower(l.catalog_id)
        AND linked."OrganizationId" = l.organization_id AND linked."KindCode" = 'service'
    CROSS JOIN LATERAL (
        SELECT count(*) AS match_count, min(c."Id"::text) AS candidate_id
        FROM "OrganizationScenarioCatalogItems" c
        WHERE c."OrganizationId" = l.organization_id AND c."KindCode" = 'service'
          AND lower(btrim(c."Name")) = lower(btrim(l.saved_name))
    ) matches
)
SELECT organization_id, maintenance_id, line_number, saved_name, catalog_id, status,
       match_count, CASE WHEN match_count = 1 THEN candidate_id END AS candidate_id
FROM candidates
ORDER BY organization_id, maintenance_id, line_number;
