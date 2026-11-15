using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    public partial class DefaultLegacyMaintenanceVatToWithoutVat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Maintenance" AS maintenance
                SET "Details" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN jsonb_typeof(item.value->'Price') = 'number'
                                AND NULLIF(btrim(item.value->>'VatRateCode'), '') IS NULL
                            THEN jsonb_set(item.value, '{VatRateCode}', '"without_vat"'::jsonb, true)
                            ELSE item.value
                        END
                        ORDER BY item.ordinality
                    )::text
                    FROM jsonb_array_elements(maintenance."Details"::jsonb)
                        WITH ORDINALITY AS item(value, ordinality)
                )
                WHERE jsonb_typeof(maintenance."Details"::jsonb) = 'array'
                    AND EXISTS (
                        SELECT 1
                        FROM jsonb_array_elements(
                            CASE WHEN jsonb_typeof(maintenance."Details"::jsonb) = 'array'
                                THEN maintenance."Details"::jsonb
                                ELSE '[]'::jsonb
                            END
                        ) AS item(value)
                        WHERE jsonb_typeof(item.value->'Price') = 'number'
                            AND NULLIF(btrim(item.value->>'VatRateCode'), '') IS NULL
                    );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // После заполнения нельзя отличить прежнюю пустую ставку от ставки «Без НДС», выбранной пользователем.
        }
    }
}
