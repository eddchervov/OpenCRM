using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class RecordMaterialInputUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "EnteredQuantity",
            table: "OrganizationMaterialMovements",
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "EnteredUnit",
            table: "OrganizationMaterialMovements",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ConversionFactor",
            table: "OrganizationMaterialMovements",
            type: "numeric(18,6)",
            precision: 18,
            scale: 6,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EnteredQuantity", table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "EnteredUnit", table: "OrganizationMaterialMovements");
        migrationBuilder.DropColumn(name: "ConversionFactor", table: "OrganizationMaterialMovements");
    }
}
