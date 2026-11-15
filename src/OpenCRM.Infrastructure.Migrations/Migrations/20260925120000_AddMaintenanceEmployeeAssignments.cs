using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddMaintenanceEmployeeAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "MaintenanceEmployeeAssignments" (
                "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                "OrganizationId" uuid NOT NULL,
                "MaintenanceId" uuid NOT NULL,
                "DetailId" uuid NOT NULL,
                "EmployeeId" uuid NOT NULL,
                "PlannedDate" timestamp with time zone NULL,
                "PlannedEndDate" timestamp with time zone NULL,
                "PlannedStartTime" interval NULL,
                "PlannedEndTime" interval NULL,
                "Status" integer NOT NULL,
                "IsDeleted" boolean NOT NULL
            );

            CREATE INDEX "IX_MaintenanceEmployeeAssignments_Organization_Employee_Date"
                ON "MaintenanceEmployeeAssignments" ("OrganizationId", "EmployeeId", "PlannedDate");
            CREATE INDEX "IX_MaintenanceEmployeeAssignments_Organization_Date"
                ON "MaintenanceEmployeeAssignments" ("OrganizationId", "PlannedDate");
            CREATE INDEX "IX_MaintenanceEmployeeAssignments_Organization_EndDate"
                ON "MaintenanceEmployeeAssignments" ("OrganizationId", (COALESCE("PlannedEndDate", "PlannedDate")))
                WHERE NOT "IsDeleted" AND "Status" NOT IN (3, 4);
            CREATE INDEX "IX_MaintenanceEmployeeAssignments_Organization_Employee_EndDate"
                ON "MaintenanceEmployeeAssignments" ("OrganizationId", "EmployeeId", (COALESCE("PlannedEndDate", "PlannedDate")))
                WHERE NOT "IsDeleted" AND "Status" NOT IN (3, 4);
            CREATE INDEX "IX_MaintenanceEmployeeAssignments_Maintenance"
                ON "MaintenanceEmployeeAssignments" ("MaintenanceId");

            CREATE FUNCTION "RefreshMaintenanceEmployeeAssignments"(maintenance_id uuid)
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
                    COALESCE(NULLIF(detail.value->>'DetailId', '')::uuid, gen_random_uuid()),
                    COALESCE(NULLIF(detail.value->>'EmployeeId', '')::uuid, maintenance_row."EmployeeId"),
                    maintenance_row."PlannedDate", maintenance_row."PlannedEndDate",
                    maintenance_row."PlannedStartTime", maintenance_row."PlannedEndTime",
                    maintenance_row."Status", maintenance_row."IsDeleted"
                FROM jsonb_array_elements(
                    CASE WHEN jsonb_typeof(maintenance_row."Details"::jsonb) = 'array'
                        THEN maintenance_row."Details"::jsonb ELSE '[]'::jsonb END) AS detail(value)
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

            CREATE FUNCTION "RefreshMaintenanceAssignmentsOnChange"()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    PERFORM "RefreshMaintenanceEmployeeAssignments"(OLD."Id");
                    RETURN OLD;
                END IF;

                PERFORM "RefreshMaintenanceEmployeeAssignments"(NEW."Id");
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER "TR_MaintenanceEmployeeAssignments"
                AFTER INSERT OR UPDATE OR DELETE ON "Maintenance"
                FOR EACH ROW EXECUTE FUNCTION "RefreshMaintenanceAssignmentsOnChange"();

            CREATE FUNCTION "RefreshVehicleAssignmentsOnOrganizationChange"()
            RETURNS trigger LANGUAGE plpgsql AS $function$
            DECLARE
                maintenance_id uuid;
            BEGIN
                FOR maintenance_id IN SELECT "Id" FROM "Maintenance" WHERE "VehicleId" = NEW."Id" LOOP
                    PERFORM "RefreshMaintenanceEmployeeAssignments"(maintenance_id);
                END LOOP;
                RETURN NEW;
            END;
            $function$;

            CREATE TRIGGER "TR_VehicleMaintenanceEmployeeAssignments"
                AFTER UPDATE OF "OrganizationId" ON "Vehicles"
                FOR EACH ROW WHEN (OLD."OrganizationId" IS DISTINCT FROM NEW."OrganizationId")
                EXECUTE FUNCTION "RefreshVehicleAssignmentsOnOrganizationChange"();

            SELECT "RefreshMaintenanceEmployeeAssignments"("Id") FROM "Maintenance";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER "TR_VehicleMaintenanceEmployeeAssignments" ON "Vehicles";
            DROP TRIGGER "TR_MaintenanceEmployeeAssignments" ON "Maintenance";
            DROP FUNCTION "RefreshVehicleAssignmentsOnOrganizationChange"();
            DROP FUNCTION "RefreshMaintenanceAssignmentsOnChange"();
            DROP FUNCTION "RefreshMaintenanceEmployeeAssignments"(uuid);
            DROP TABLE "MaintenanceEmployeeAssignments";
            """);
    }
}
