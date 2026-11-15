using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FixVehicleCatalogSuggestionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehicleCatalogSuggestions_Kind_BrandName_ModelName_Status",
                table: "VehicleCatalogSuggestions");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCatalogSuggestions_Kind_BrandName_ModelName_Status",
                table: "VehicleCatalogSuggestions",
                columns: new[] { "Kind", "BrandName", "ModelName", "Status" },
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "VehicleCatalogSuggestions"
                        GROUP BY "Kind", "BrandName", "ModelName", "Status"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Нельзя восстановить уникальный индекс предложений: есть повторяющиеся исторические записи.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_VehicleCatalogSuggestions_Kind_BrandName_ModelName_Status",
                table: "VehicleCatalogSuggestions");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCatalogSuggestions_Kind_BrandName_ModelName_Status",
                table: "VehicleCatalogSuggestions",
                columns: new[] { "Kind", "BrandName", "ModelName", "Status" },
                unique: true);
        }
    }
}
