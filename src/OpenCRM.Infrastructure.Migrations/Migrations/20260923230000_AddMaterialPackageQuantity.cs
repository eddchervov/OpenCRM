using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddMaterialPackageQuantity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PackageQuantity",
            table: "OrganizationMaterialStocks",
            type: "numeric(18,3)",
            precision: 18,
            scale: 3,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PackageQuantity", table: "OrganizationMaterialStocks");
    }
}
