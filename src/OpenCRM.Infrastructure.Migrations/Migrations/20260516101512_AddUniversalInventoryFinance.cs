using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalInventoryFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageUnitCost",
                table: "OrganizationMaterialStocks",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryValue",
                table: "OrganizationMaterialStocks",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQuantity",
                table: "OrganizationMaterialStocks",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostBalanceAfter",
                table: "OrganizationMaterialMovements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityBalanceAfter",
                table: "OrganizationMaterialMovements",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEntityId",
                table: "OrganizationMaterialMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEntityType",
                table: "OrganizationMaterialMovements",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "OrganizationMaterialMovements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "OrganizationMaterialMovements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Maintenance",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LaborCostAmount",
                table: "Maintenance",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialCostAmount",
                table: "Maintenance",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Maintenance",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_OrganizationId_SourceEntityTy~",
                table: "OrganizationMaterialMovements",
                columns: new[] { "OrganizationId", "SourceEntityType", "SourceEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationMaterialMovements_OrganizationId_SourceEntityTy~",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "AverageUnitCost",
                table: "OrganizationMaterialStocks");

            migrationBuilder.DropColumn(
                name: "InventoryValue",
                table: "OrganizationMaterialStocks");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "OrganizationMaterialStocks");

            migrationBuilder.DropColumn(
                name: "CostBalanceAfter",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "QuantityBalanceAfter",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "SourceEntityId",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "SourceEntityType",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "LaborCostAmount",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "MaterialCostAmount",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Maintenance");
        }
    }
}
