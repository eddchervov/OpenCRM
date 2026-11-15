using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Добавляет таблицы складских остатков и движений материалов организации.
    /// </summary>
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260510170000_AddOrganizationMaterialInventory")]
    public partial class AddOrganizationMaterialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationMaterialStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMaterialStocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialStocks_OrganizationScenarioCatalogItems_C~",
                        column: x => x.CatalogItemId,
                        principalTable: "OrganizationScenarioCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialStocks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMaterialMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TypeCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMaterialMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialMovements_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialMovements_OrganizationScenarioCatalogItems~",
                        column: x => x.CatalogItemId,
                        principalTable: "OrganizationScenarioCatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialMovements_OrganizationMaterialStocks_Stock~",
                        column: x => x.StockId,
                        principalTable: "OrganizationMaterialStocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMaterialMovements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_CatalogItemId",
                table: "OrganizationMaterialMovements",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_MaintenanceId_TypeCode",
                table: "OrganizationMaterialMovements",
                columns: new[] { "MaintenanceId", "TypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_OrganizationId_CreatedAt",
                table: "OrganizationMaterialMovements",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_OrganizationId_StockId_CreatedAt",
                table: "OrganizationMaterialMovements",
                columns: new[] { "OrganizationId", "StockId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialMovements_StockId",
                table: "OrganizationMaterialMovements",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialStocks_CatalogItemId",
                table: "OrganizationMaterialStocks",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialStocks_OrganizationId_CatalogItemId",
                table: "OrganizationMaterialStocks",
                columns: new[] { "OrganizationId", "CatalogItemId" },
                unique: true,
                filter: "\"CatalogItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMaterialStocks_OrganizationId_Name_Unit",
                table: "OrganizationMaterialStocks",
                columns: new[] { "OrganizationId", "Name", "Unit" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationMaterialMovements");

            migrationBuilder.DropTable(
                name: "OrganizationMaterialStocks");
        }
    }
}
