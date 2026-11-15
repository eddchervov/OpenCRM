using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddMaintenanceDetailIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Maintenance" AS maintenance
            SET "Details" = (
                SELECT jsonb_agg(
                    CASE WHEN item.value ? 'DetailId'
                        THEN item.value
                        ELSE item.value || jsonb_build_object('DetailId', gen_random_uuid())
                    END ORDER BY item.ordinal)::text
                FROM jsonb_array_elements(maintenance."Details"::jsonb) WITH ORDINALITY AS item(value, ordinal)
            )
            WHERE maintenance."Details" IS NOT NULL
                AND jsonb_typeof(maintenance."Details"::jsonb) = 'array'
                AND EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements(maintenance."Details"::jsonb) AS item(value)
                    WHERE NOT item.value ? 'DetailId'
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Maintenance" AS maintenance
            SET "Details" = (
                SELECT jsonb_agg(item.value - 'DetailId' ORDER BY item.ordinal)::text
                FROM jsonb_array_elements(maintenance."Details"::jsonb) WITH ORDINALITY AS item(value, ordinal)
            )
            WHERE maintenance."Details" IS NOT NULL
                AND jsonb_typeof(maintenance."Details"::jsonb) = 'array'
                AND EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements(maintenance."Details"::jsonb) AS item(value)
                    WHERE item.value ? 'DetailId'
                );
            """);
    }
}
