using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class RefreshRelationalMaintenanceAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION "RefreshMaintenanceEmployeeAssignments"(maintenance_id uuid)
            RETURNS void LANGUAGE plpgsql AS $function$
            DECLARE
                maintenance_row "Maintenance"%ROWTYPE;
                organization_id uuid;
                assigned_count integer;
            BEGIN
                DELETE FROM "MaintenanceEmployeeAssignments"
                WHERE "MaintenanceId" = maintenance_id;

                SELECT * INTO maintenance_row FROM "Maintenance" WHERE "Id" = maintenance_id;
                IF NOT FOUND THEN
                    RETURN;
                END IF;

                SELECT "OrganizationId" INTO organization_id
                FROM "Vehicles" WHERE "Id" = maintenance_row."VehicleId";
                IF organization_id IS NULL THEN
                    RETURN;
                END IF;

                INSERT INTO "MaintenanceEmployeeAssignments" (
                    "OrganizationId", "MaintenanceId", "DetailId", "EmployeeId",
                    "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "IsDeleted")
                SELECT organization_id, maintenance_id,
                    detail."DetailId",
                    COALESCE(detail."EmployeeId", maintenance_row."EmployeeId"),
                    maintenance_row."PlannedDate", maintenance_row."PlannedEndDate",
                    maintenance_row."PlannedStartTime", maintenance_row."PlannedEndTime",
                    maintenance_row."Status", maintenance_row."IsDeleted"
                FROM "MaintenanceDetails" AS detail
                WHERE detail."MaintenanceId" = maintenance_id
                    AND detail."MaterialCatalogItemId" IS NULL
                    AND detail."MaterialName" IS NULL
                    AND detail."MaterialQuantity" IS NULL
                    AND COALESCE(detail."EmployeeId", maintenance_row."EmployeeId") IS NOT NULL;

                GET DIAGNOSTICS assigned_count = ROW_COUNT;
                IF assigned_count = 0 AND maintenance_row."EmployeeId" IS NOT NULL THEN
                    INSERT INTO "MaintenanceEmployeeAssignments" (
                        "OrganizationId", "MaintenanceId", "DetailId", "EmployeeId",
                        "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "IsDeleted")
                    VALUES (
                        organization_id, maintenance_id, '00000000-0000-0000-0000-000000000000', maintenance_row."EmployeeId",
                        maintenance_row."PlannedDate", maintenance_row."PlannedEndDate",
                        maintenance_row."PlannedStartTime", maintenance_row."PlannedEndTime",
                        maintenance_row."Status", maintenance_row."IsDeleted");
                END IF;
            END;
            $function$;

            CREATE FUNCTION "RefreshMaintenanceAssignmentsOnDetailChange"()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    PERFORM "RefreshMaintenanceEmployeeAssignments"(OLD."MaintenanceId");
                    RETURN OLD;
                END IF;

                IF TG_OP = 'UPDATE' AND OLD."MaintenanceId" IS DISTINCT FROM NEW."MaintenanceId" THEN
                    PERFORM "RefreshMaintenanceEmployeeAssignments"(OLD."MaintenanceId");
                END IF;

                PERFORM "RefreshMaintenanceEmployeeAssignments"(NEW."MaintenanceId");
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER "TR_MaintenanceDetailEmployeeAssignments"
                AFTER INSERT OR UPDATE OR DELETE ON "MaintenanceDetails"
                FOR EACH ROW EXECUTE FUNCTION "RefreshMaintenanceAssignmentsOnDetailChange"();

            SELECT "RefreshMaintenanceEmployeeAssignments"("Id") FROM "Maintenance";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Поддерживаем откат как до реляционных строк, так и до прежнего JSON-поля.
        migrationBuilder.Sql("""
            DROP TRIGGER "TR_MaintenanceDetailEmployeeAssignments" ON "MaintenanceDetails";
            DROP FUNCTION "RefreshMaintenanceAssignmentsOnDetailChange"();

            CREATE OR REPLACE FUNCTION "RefreshMaintenanceEmployeeAssignments"(maintenance_id uuid)
            RETURNS void LANGUAGE plpgsql AS $function$
            DECLARE
                maintenance_row "Maintenance"%ROWTYPE;
                organization_id uuid;
                assigned_count integer;
                details_json jsonb;
            BEGIN
                DELETE FROM "MaintenanceEmployeeAssignments"
                WHERE "MaintenanceId" = maintenance_id;

                SELECT * INTO maintenance_row FROM "Maintenance" WHERE "Id" = maintenance_id;
                IF NOT FOUND THEN
                    RETURN;
                END IF;

                SELECT "OrganizationId" INTO organization_id
                FROM "Vehicles" WHERE "Id" = maintenance_row."VehicleId";
                IF organization_id IS NULL THEN
                    RETURN;
                END IF;

                IF to_jsonb(maintenance_row) ? 'Details' THEN
                    details_json := (to_jsonb(maintenance_row)->>'Details')::jsonb;
                ELSE
                    SELECT COALESCE(jsonb_agg(to_jsonb(detail) ORDER BY detail."Position"), '[]'::jsonb)
                    INTO details_json FROM "MaintenanceDetails" detail
                    WHERE detail."MaintenanceId" = maintenance_id;
                END IF;

                INSERT INTO "MaintenanceEmployeeAssignments" (
                    "OrganizationId", "MaintenanceId", "DetailId", "EmployeeId",
                    "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "IsDeleted")
                SELECT organization_id, maintenance_id,
                    COALESCE(NULLIF(detail.value->>'DetailId', '')::uuid, gen_random_uuid()),
                    COALESCE(NULLIF(detail.value->>'EmployeeId', '')::uuid, maintenance_row."EmployeeId"),
                    maintenance_row."PlannedDate", maintenance_row."PlannedEndDate",
                    maintenance_row."PlannedStartTime", maintenance_row."PlannedEndTime",
                    maintenance_row."Status", maintenance_row."IsDeleted"
                FROM jsonb_array_elements(
                    CASE WHEN jsonb_typeof(details_json) = 'array'
                        THEN details_json ELSE '[]'::jsonb END) AS detail(value)
                WHERE detail.value->>'MaterialCatalogItemId' IS NULL
                    AND detail.value->>'MaterialName' IS NULL
                    AND detail.value->>'MaterialQuantity' IS NULL
                    AND COALESCE(NULLIF(detail.value->>'EmployeeId', '')::uuid, maintenance_row."EmployeeId") IS NOT NULL;

                GET DIAGNOSTICS assigned_count = ROW_COUNT;
                IF assigned_count = 0 AND maintenance_row."EmployeeId" IS NOT NULL THEN
                    INSERT INTO "MaintenanceEmployeeAssignments" (
                        "OrganizationId", "MaintenanceId", "DetailId", "EmployeeId",
                        "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "IsDeleted")
                    VALUES (
                        organization_id, maintenance_id, '00000000-0000-0000-0000-000000000000', maintenance_row."EmployeeId",
                        maintenance_row."PlannedDate", maintenance_row."PlannedEndDate",
                        maintenance_row."PlannedStartTime", maintenance_row."PlannedEndTime",
                        maintenance_row."Status", maintenance_row."IsDeleted");
                END IF;
            END;
            $function$;
            """);
    }
}