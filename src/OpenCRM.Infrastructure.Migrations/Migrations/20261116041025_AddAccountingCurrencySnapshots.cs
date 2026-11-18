using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingCurrencySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_OrganizationServiceHourlyTotals",
                table: "OrganizationServiceHourlyTotals");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "OrganizationServiceHourlyTotals",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "OrganizationScenarioCatalogItems",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Organizations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "OrganizationMaterialStocks",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "OrganizationMaterialMovements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "MaintenancePayments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "MaintenanceFinancialTotals",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "MaintenanceDocuments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Maintenance",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddColumn<string>(
                name: "PersonalCurrencyCode",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrganizationServiceHourlyTotals",
                table: "OrganizationServiceHourlyTotals",
                columns: new[] { "OrganizationId", "UtcHour", "CurrencyCode", "NameHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "Organizations", "Maintenance", "MaintenancePayments", "MaintenanceDocuments",
                    "MaintenanceFinancialTotals", "OrganizationServiceHourlyTotals", "OrganizationScenarioCatalogItems",
                    "OrganizationMaterialStocks", "OrganizationMaterialMovements", identity."AspNetUsers"
                    IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Organizations" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "Maintenance" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "MaintenancePayments" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "MaintenanceDocuments" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "MaintenanceFinancialTotals" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "OrganizationServiceHourlyTotals" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "OrganizationScenarioCatalogItems" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "OrganizationMaterialStocks" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM "OrganizationMaterialMovements" WHERE "CurrencyCode" <> 'RUB')
                        OR EXISTS (SELECT 1 FROM identity."AspNetUsers" WHERE "PersonalCurrencyCode" <> 'RUB')
                    THEN
                        RAISE EXCEPTION 'Откат невозможен: сохранены настройки или суммы в валюте, отличной от RUB.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrganizationServiceHourlyTotals",
                table: "OrganizationServiceHourlyTotals");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "OrganizationServiceHourlyTotals");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "OrganizationScenarioCatalogItems");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "OrganizationMaterialStocks");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "OrganizationMaterialMovements");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "MaintenanceFinancialTotals");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "MaintenanceDocuments");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "PersonalCurrencyCode",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrganizationServiceHourlyTotals",
                table: "OrganizationServiceHourlyTotals",
                columns: new[] { "OrganizationId", "UtcHour", "NameHash" });
        }
    }
}
