using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Сохраняет необратимый признак успешного использования позиции каталога. </summary>
    public partial class PersistCatalogFirstUse : Migration
    {
        /// <summary> Добавляет признак, транзакционные триггеры и однократно обрабатывает существующую историю. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBeenUsed",
                table: "OrganizationScenarioCatalogItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION opencrm_preserve_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        NEW."HasBeenUsed" := OLD."HasBeenUsed" OR NEW."HasBeenUsed";
                        RETURN NEW;
                    END IF;

                    IF OLD."HasBeenUsed" AND OLD."KindCode" NOT IN ('service', 'service_bundle')
                        AND EXISTS (SELECT 1 FROM "Organizations" WHERE "Id" = OLD."OrganizationId") THEN
                        RAISE EXCEPTION 'Использованную позицию каталога нельзя удалить.' USING ERRCODE = '23514';
                    END IF;

                    RETURN OLD;
                END;
                $$;

                CREATE TRIGGER opencrm_preserve_catalog_usage
                BEFORE UPDATE OR DELETE ON "OrganizationScenarioCatalogItems"
                FOR EACH ROW EXECUTE FUNCTION opencrm_preserve_catalog_usage();

                CREATE FUNCTION opencrm_mark_direct_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    EXECUTE format(
                        'UPDATE "OrganizationScenarioCatalogItems" AS catalog
                         SET "HasBeenUsed" = true
                         FROM (SELECT DISTINCT "OrganizationId", %I AS id FROM new_catalog_usage_rows) AS used
                         WHERE catalog."Id" = used.id AND catalog."OrganizationId" = used."OrganizationId"
                           AND NOT catalog."HasBeenUsed"', TG_ARGV[0]);

                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION opencrm_mark_detail_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    -- Блокировка исключает пропуск незавершённых строк при одновременном переносе личной техники.
                    PERFORM vehicle."Id"
                    FROM "Vehicles" AS vehicle
                    WHERE vehicle."Id" IN (
                        SELECT maintenance."VehicleId"
                        FROM new_catalog_usage_rows AS detail
                        JOIN "Maintenance" AS maintenance ON maintenance."Id" = detail."MaintenanceId"
                        WHERE detail."CatalogItemId" IS NOT NULL OR detail."MaterialCatalogItemId" IS NOT NULL)
                    ORDER BY vehicle."Id"
                    FOR NO KEY UPDATE;

                    UPDATE "OrganizationScenarioCatalogItems" AS catalog
                    SET "HasBeenUsed" = true
                    FROM (
                        SELECT DISTINCT reference.id, vehicle."OrganizationId"
                        FROM new_catalog_usage_rows AS detail
                        JOIN "Maintenance" AS maintenance ON maintenance."Id" = detail."MaintenanceId"
                        JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                        CROSS JOIN LATERAL (VALUES (detail."CatalogItemId"), (detail."MaterialCatalogItemId")) AS reference(id)
                        WHERE reference.id IS NOT NULL AND vehicle."OrganizationId" IS NOT NULL
                    ) AS used
                    WHERE catalog."Id" = used.id AND catalog."OrganizationId" = used."OrganizationId"
                      AND NOT catalog."HasBeenUsed";

                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION opencrm_mark_order_catalog_usage(maintenance_ids uuid[]) RETURNS void LANGUAGE plpgsql AS $$
                BEGIN
                    IF maintenance_ids IS NULL THEN
                        RETURN;
                    END IF;

                    -- Следующая команда читает организацию после ожидания переноса, а перенос ждёт фиксации новых использований.
                    PERFORM vehicle."Id"
                    FROM "Vehicles" AS vehicle
                    WHERE vehicle."Id" IN (
                        SELECT maintenance."VehicleId"
                        FROM unnest(maintenance_ids) AS batch(id)
                        CROSS JOIN LATERAL (
                            SELECT "VehicleId" FROM "Maintenance" WHERE "Id" = batch.id OFFSET 0
                        ) AS maintenance)
                    ORDER BY vehicle."Id"
                    FOR NO KEY UPDATE;

                    -- OFFSET 0 сохраняет поиск по PK для каждой записи порции вместо повторного полного чтения деталей.
                    UPDATE "OrganizationScenarioCatalogItems" AS catalog
                    SET "HasBeenUsed" = true
                    FROM (
                        SELECT DISTINCT reference.id, vehicle."OrganizationId"
                        FROM unnest(maintenance_ids) AS batch(id)
                        CROSS JOIN LATERAL (
                            SELECT "VehicleId" FROM "Maintenance" WHERE "Id" = batch.id OFFSET 0
                        ) AS maintenance
                        JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                        CROSS JOIN LATERAL (
                            SELECT "CatalogItemId", "MaterialCatalogItemId" FROM "MaintenanceDetails"
                            WHERE "MaintenanceId" = batch.id OFFSET 0
                        ) AS detail
                        CROSS JOIN LATERAL (VALUES (detail."CatalogItemId"), (detail."MaterialCatalogItemId")) AS reference(id)
                        WHERE reference.id IS NOT NULL AND vehicle."OrganizationId" IS NOT NULL
                        UNION
                        SELECT post."Id", vehicle."OrganizationId"
                        FROM unnest(maintenance_ids) AS batch(id)
                        CROSS JOIN LATERAL (
                            SELECT "VehicleId", "Location" FROM "Maintenance" WHERE "Id" = batch.id OFFSET 0
                        ) AS maintenance
                        JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                        JOIN "OrganizationScenarioCatalogItems" AS post
                          ON post."OrganizationId" = vehicle."OrganizationId" AND post."KindCode" = 'work_post'
                         AND upper(btrim(post."Name", E' \t\n\r')) = upper(btrim(maintenance."Location", E' \t\n\r'))
                        WHERE NOT post."HasBeenUsed" AND maintenance."Location" IS NOT NULL
                    ) AS used
                    WHERE catalog."Id" = used.id AND catalog."OrganizationId" = used."OrganizationId"
                      AND NOT catalog."HasBeenUsed";
                END;
                $$;

                CREATE FUNCTION opencrm_mark_maintenance_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE maintenance_ids uuid[];
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        SELECT array_agg("Id") INTO maintenance_ids FROM new_catalog_usage_rows;
                    ELSE
                        SELECT array_agg(current."Id") INTO maintenance_ids
                        FROM new_catalog_usage_rows AS current
                        JOIN old_catalog_usage_rows AS previous ON previous."Id" = current."Id"
                        WHERE current."VehicleId" IS DISTINCT FROM previous."VehicleId"
                           OR current."Location" IS DISTINCT FROM previous."Location";
                    END IF;

                    PERFORM opencrm_mark_order_catalog_usage(maintenance_ids);

                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION opencrm_mark_vehicle_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE maintenance_ids uuid[];
                BEGIN
                    -- Перенос личной техники сериализуется с созданием и удалением справочника целевой организации.
                    PERFORM organization."Id"
                    FROM "Organizations" AS organization
                    WHERE organization."Id" IN (
                        SELECT current."OrganizationId"
                        FROM new_catalog_usage_rows AS current
                        JOIN old_catalog_usage_rows AS previous ON previous."Id" = current."Id"
                        WHERE current."OrganizationId" IS DISTINCT FROM previous."OrganizationId")
                    ORDER BY organization."Id"
                    FOR NO KEY UPDATE;

                    SELECT array_agg(maintenance."Id") INTO maintenance_ids
                    FROM new_catalog_usage_rows AS current
                    JOIN old_catalog_usage_rows AS previous ON previous."Id" = current."Id"
                    JOIN "Maintenance" AS maintenance ON maintenance."VehicleId" = current."Id"
                    WHERE current."OrganizationId" IS DISTINCT FROM previous."OrganizationId";
                    PERFORM opencrm_mark_order_catalog_usage(maintenance_ids);

                    RETURN NULL;
                END;
                $$;

                CREATE FUNCTION opencrm_reconcile_catalog_usage() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        -- Новая организация без истории не требует поиска среди исторических идентификаторов других организаций.
                        IF EXISTS (
                            SELECT 1 FROM new_catalog_usage_rows AS restored
                            JOIN "Vehicles" AS vehicle ON vehicle."OrganizationId" = restored."OrganizationId"
                            JOIN "Maintenance" AS maintenance ON maintenance."VehicleId" = vehicle."Id") THEN
                            UPDATE "OrganizationScenarioCatalogItems" AS catalog
                            SET "HasBeenUsed" = true
                            FROM new_catalog_usage_rows AS restored
                            WHERE catalog."Id" = restored."Id" AND NOT catalog."HasBeenUsed"
                              AND EXISTS (
                                  SELECT 1 FROM "Vehicles" AS vehicle
                                  JOIN "Maintenance" AS maintenance ON maintenance."VehicleId" = vehicle."Id"
                                  JOIN "MaintenanceDetails" AS detail ON detail."MaintenanceId" = maintenance."Id"
                                  WHERE vehicle."OrganizationId" = restored."OrganizationId"
                                    AND (detail."CatalogItemId" = restored."Id"
                                      OR detail."MaterialCatalogItemId" = restored."Id"));
                        END IF;
                    END IF;

                    IF TG_OP = 'UPDATE' THEN
                        IF NOT EXISTS (
                            SELECT 1 FROM new_catalog_usage_rows AS current
                            JOIN old_catalog_usage_rows AS previous ON previous."Id" = current."Id"
                            WHERE NOT current."HasBeenUsed" AND current."KindCode" = 'work_post'
                              AND (current."Name" IS DISTINCT FROM previous."Name"
                                OR current."OrganizationId" IS DISTINCT FROM previous."OrganizationId"
                                OR current."KindCode" IS DISTINCT FROM previous."KindCode")) THEN
                            RETURN NULL;
                        END IF;
                    END IF;

                    UPDATE "OrganizationScenarioCatalogItems" AS catalog
                    SET "HasBeenUsed" = true
                    FROM new_catalog_usage_rows AS post
                    WHERE catalog."Id" = post."Id" AND post."KindCode" = 'work_post' AND NOT catalog."HasBeenUsed"
                      AND EXISTS (
                          SELECT 1 FROM "Vehicles" AS vehicle
                          JOIN "Maintenance" AS maintenance ON maintenance."VehicleId" = vehicle."Id"
                          WHERE vehicle."OrganizationId" = post."OrganizationId"
                            AND upper(btrim(maintenance."Location", E' \t\n\r')) = upper(btrim(post."Name", E' \t\n\r')));

                    RETURN NULL;
                END;
                $$;
                """);

            var directReferenceTables = new[]
            {
                ("OrganizationMaterialStocks", "CatalogItemId"),
                ("OrganizationMaterialMovements", "CatalogItemId"),
                ("OrganizationServiceBundleItems", "ServiceId")
            };

            foreach (var (table, column) in directReferenceTables)
            {
                foreach (var operation in new[] { "INSERT", "UPDATE" })
                {
                    migrationBuilder.Sql($"""
                        CREATE TRIGGER opencrm_catalog_usage_{operation.ToLowerInvariant()}
                        AFTER {operation} ON "{table}"
                        REFERENCING NEW TABLE AS new_catalog_usage_rows
                        FOR EACH STATEMENT EXECUTE FUNCTION opencrm_mark_direct_catalog_usage('{column}');
                        """);
                }
            }

            var triggerFunctions = new[]
            {
                ("MaintenanceDetails", "opencrm_mark_detail_catalog_usage"),
                ("Maintenance", "opencrm_mark_maintenance_catalog_usage"),
                ("OrganizationScenarioCatalogItems", "opencrm_reconcile_catalog_usage")
            };

            foreach (var (table, function) in triggerFunctions)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER opencrm_catalog_usage_insert
                    AFTER INSERT ON "{table}"
                    REFERENCING NEW TABLE AS new_catalog_usage_rows
                    FOR EACH STATEMENT EXECUTE FUNCTION {function}();

                    CREATE TRIGGER opencrm_catalog_usage_update
                    AFTER UPDATE ON "{table}"
                    REFERENCING OLD TABLE AS old_catalog_usage_rows NEW TABLE AS new_catalog_usage_rows
                    FOR EACH STATEMENT EXECUTE FUNCTION {function}();
                    """);
            }

            migrationBuilder.Sql(
                """
                CREATE TRIGGER opencrm_catalog_usage_update
                AFTER UPDATE ON "Vehicles"
                REFERENCING OLD TABLE AS old_catalog_usage_rows NEW TABLE AS new_catalog_usage_rows
                FOR EACH STATEMENT EXECUTE FUNCTION opencrm_mark_vehicle_catalog_usage();

                UPDATE "OrganizationScenarioCatalogItems" AS catalog
                SET "HasBeenUsed" = true
                FROM (
                    SELECT "OrganizationId", "CatalogItemId" AS id FROM "OrganizationMaterialStocks"
                    UNION
                    SELECT "OrganizationId", "CatalogItemId" FROM "OrganizationMaterialMovements"
                    UNION
                    SELECT "OrganizationId", "ServiceId" FROM "OrganizationServiceBundleItems"
                ) AS used
                WHERE catalog."Id" = used.id AND catalog."OrganizationId" = used."OrganizationId"
                  AND NOT catalog."HasBeenUsed";

                -- Материализация вычисляет общую историю один раз, исключая отдельный полный просмотр для каждой позиции каталога.
                WITH used AS MATERIALIZED (
                    SELECT DISTINCT reference.id, vehicle."OrganizationId"
                    FROM "Maintenance" AS maintenance
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    JOIN "MaintenanceDetails" AS detail ON detail."MaintenanceId" = maintenance."Id"
                    CROSS JOIN LATERAL (VALUES (detail."CatalogItemId"), (detail."MaterialCatalogItemId")) AS reference(id)
                    WHERE reference.id IS NOT NULL AND vehicle."OrganizationId" IS NOT NULL
                    UNION
                    SELECT DISTINCT post."Id", vehicle."OrganizationId"
                    FROM "Maintenance" AS maintenance
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    JOIN "OrganizationScenarioCatalogItems" AS post
                      ON post."OrganizationId" = vehicle."OrganizationId" AND post."KindCode" = 'work_post'
                     AND upper(btrim(post."Name", E' \t\n\r')) = upper(btrim(maintenance."Location", E' \t\n\r'))
                    WHERE maintenance."Location" IS NOT NULL
                )
                UPDATE "OrganizationScenarioCatalogItems" AS catalog
                SET "HasBeenUsed" = true
                FROM used
                WHERE catalog."Id" = used.id AND catalog."OrganizationId" = used."OrganizationId"
                  AND NOT catalog."HasBeenUsed";
                """);
        }

        /// <summary> Удаляет признак и триггеры; утраченное после удаления ссылок первое использование восстановить нельзя. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var tables = new[]
            {
                "OrganizationMaterialStocks", "OrganizationMaterialMovements", "OrganizationServiceBundleItems",
                "MaintenanceDetails", "Maintenance", "OrganizationScenarioCatalogItems", "Vehicles"
            };

            foreach (var table in tables)
            {
                migrationBuilder.Sql($"""
                    DROP TRIGGER IF EXISTS opencrm_catalog_usage_insert ON "{table}";
                    DROP TRIGGER IF EXISTS opencrm_catalog_usage_update ON "{table}";
                    """);
            }

            migrationBuilder.Sql(
                """
                DROP TRIGGER opencrm_preserve_catalog_usage ON "OrganizationScenarioCatalogItems";
                DROP FUNCTION opencrm_preserve_catalog_usage();
                DROP FUNCTION opencrm_mark_direct_catalog_usage();
                DROP FUNCTION opencrm_mark_detail_catalog_usage();
                DROP FUNCTION opencrm_mark_maintenance_catalog_usage();
                DROP FUNCTION opencrm_mark_vehicle_catalog_usage();
                DROP FUNCTION opencrm_mark_order_catalog_usage(uuid[]);
                DROP FUNCTION opencrm_reconcile_catalog_usage();
                """);

            migrationBuilder.DropColumn(
                name: "HasBeenUsed",
                table: "OrganizationScenarioCatalogItems");
        }
    }
}
