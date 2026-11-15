using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class NormalizeMaterialBaseUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "AverageUnitCost",
            table: "OrganizationMaterialStocks",
            type: "numeric(18,8)",
            precision: 18,
            scale: 8,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4);
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitCost",
            table: "OrganizationMaterialMovements",
            type: "numeric(18,8)",
            precision: 18,
            scale: 8,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldPrecision: 18,
            oldScale: 4);
        migrationBuilder.AddColumn<string>(
            name: "LegacyUnit",
            table: "OrganizationMaterialStocks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "OrganizationMaterialMovements" AS movement
            SET "EnteredQuantity" = COALESCE(movement."EnteredQuantity", ABS(movement."Quantity")),
                "EnteredUnit" = COALESCE(movement."EnteredUnit", movement."Unit"),
                "ConversionFactor" = COALESCE(movement."ConversionFactor" * 1000, 1000),
                "Quantity" = movement."Quantity" * 1000,
                "QuantityBalanceAfter" = movement."QuantityBalanceAfter" * 1000,
                "UnitCost" = movement."UnitCost" / 1000,
                "Unit" = CASE stock."Unit" WHEN 'кг' THEN 'г' ELSE 'мл' END
            FROM "OrganizationMaterialStocks" AS stock
            WHERE movement."StockId" = stock."Id" AND stock."Unit" IN ('кг', 'л')
                AND NOT EXISTS (
                    SELECT 1 FROM "OrganizationMaterialStocks" AS existing
                    WHERE existing."OrganizationId" = stock."OrganizationId"
                        AND existing."Name" = stock."Name"
                        AND existing."Unit" = CASE stock."Unit" WHEN 'кг' THEN 'г' ELSE 'мл' END);
            """);
        migrationBuilder.Sql("""
            UPDATE "OrganizationMaterialStocks" AS stock
            SET "LegacyUnit" = "Unit",
                "QuantityOnHand" = "QuantityOnHand" * 1000,
                "ReservedQuantity" = "ReservedQuantity" * 1000,
                "MinQuantity" = "MinQuantity" * 1000,
                "AverageUnitCost" = "AverageUnitCost" / 1000,
                "Unit" = CASE "Unit" WHEN 'кг' THEN 'г' ELSE 'мл' END
            WHERE stock."Unit" IN ('кг', 'л')
                AND NOT EXISTS (
                    SELECT 1 FROM "OrganizationMaterialStocks" AS existing
                    WHERE existing."OrganizationId" = stock."OrganizationId"
                        AND existing."Name" = stock."Name"
                        AND existing."Unit" = CASE stock."Unit" WHEN 'кг' THEN 'г' ELSE 'мл' END);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "OrganizationMaterialMovements" AS movement
            SET "ConversionFactor" = movement."ConversionFactor" / 1000,
                "Quantity" = movement."Quantity" / 1000,
                "QuantityBalanceAfter" = movement."QuantityBalanceAfter" / 1000,
                "UnitCost" = movement."UnitCost" * 1000,
                "Unit" = stock."LegacyUnit"
            FROM "OrganizationMaterialStocks" AS stock
            WHERE movement."StockId" = stock."Id" AND stock."LegacyUnit" IN ('кг', 'л');
            """);
        migrationBuilder.Sql("""
            UPDATE "OrganizationMaterialStocks"
            SET "QuantityOnHand" = "QuantityOnHand" / 1000,
                "ReservedQuantity" = "ReservedQuantity" / 1000,
                "MinQuantity" = "MinQuantity" / 1000,
                "AverageUnitCost" = "AverageUnitCost" * 1000,
                "Unit" = "LegacyUnit"
            WHERE "LegacyUnit" IN ('кг', 'л');
            """);

        migrationBuilder.DropColumn(name: "LegacyUnit", table: "OrganizationMaterialStocks");
        migrationBuilder.AlterColumn<decimal>(
            name: "AverageUnitCost",
            table: "OrganizationMaterialStocks",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,8)",
            oldPrecision: 18,
            oldScale: 8);
        migrationBuilder.AlterColumn<decimal>(
            name: "UnitCost",
            table: "OrganizationMaterialMovements",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,8)",
            oldPrecision: 18,
            oldScale: 8);
    }
}
