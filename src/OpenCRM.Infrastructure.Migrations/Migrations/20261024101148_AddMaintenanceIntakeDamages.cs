using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceIntakeDamages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntakeDiagramVersion",
                table: "Maintenance",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MaintenancePhotos_MaintenanceId_Id",
                table: "MaintenancePhotos",
                columns: new[] { "MaintenanceId", "Id" });

            migrationBuilder.CreateTable(
                name: "MaintenanceIntakeDamages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ViewCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DamageType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceIntakeDamages", x => x.Id);
                    table.UniqueConstraint("AK_MaintenanceIntakeDamages_MaintenanceId_Id", x => new { x.MaintenanceId, x.Id });
                    table.CheckConstraint("CK_MaintenanceIntakeDamages_Number", "\"Number\" > 0");
                    table.CheckConstraint("CK_MaintenanceIntakeDamages_Position", "\"X\" >= 0 AND \"X\" <= 1 AND \"Y\" >= 0 AND \"Y\" <= 1");
                    table.CheckConstraint("CK_MaintenanceIntakeDamages_Type", "\"DamageType\" IN ('scratch', 'chip', 'dent', 'other')");
                    table.CheckConstraint("CK_MaintenanceIntakeDamages_View", "\"ViewCode\" IN ('front', 'rear', 'left', 'right')");
                    table.ForeignKey(
                        name: "FK_MaintenanceIntakeDamages_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceIntakeDamagePhotos",
                columns: table => new
                {
                    DamageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceIntakeDamagePhotos", x => new { x.DamageId, x.PhotoId });
                    table.ForeignKey(
                        name: "FK_MaintenanceIntakeDamagePhotos_MaintenanceIntakeDamages_Main~",
                        columns: x => new { x.MaintenanceId, x.DamageId },
                        principalTable: "MaintenanceIntakeDamages",
                        principalColumns: new[] { "MaintenanceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceIntakeDamagePhotos_MaintenancePhotos_Maintenance~",
                        columns: x => new { x.MaintenanceId, x.PhotoId },
                        principalTable: "MaintenancePhotos",
                        principalColumns: new[] { "MaintenanceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntakeDamagePhotos_MaintenanceId_DamageId",
                table: "MaintenanceIntakeDamagePhotos",
                columns: new[] { "MaintenanceId", "DamageId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntakeDamagePhotos_MaintenanceId_PhotoId",
                table: "MaintenanceIntakeDamagePhotos",
                columns: new[] { "MaintenanceId", "PhotoId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceIntakeDamages_MaintenanceId_Number",
                table: "MaintenanceIntakeDamages",
                columns: new[] { "MaintenanceId", "Number" },
                unique: true);

            // Существующие осмотры остаются на исходной схеме, включая удалённые метки.
            migrationBuilder.Sql("""
                UPDATE "Maintenance" AS maintenance
                SET "IntakeDiagramVersion" = 'sedan-v1'
                WHERE maintenance."IntakeRecordedAt" IS NOT NULL
                   OR EXISTS (SELECT 1 FROM "MaintenanceIntakeDamages" AS damage
                              WHERE damage."MaintenanceId" = maintenance."Id");

                CREATE FUNCTION prevent_intake_diagram_version_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."IntakeDiagramVersion" IS NOT NULL
                       AND NEW."IntakeDiagramVersion" IS DISTINCT FROM OLD."IntakeDiagramVersion" THEN
                        RAISE EXCEPTION 'Версия схемы сохранённого осмотра неизменяема.' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_Maintenance_IntakeDiagramVersion"
                BEFORE UPDATE OF "IntakeDiagramVersion" ON "Maintenance"
                FOR EACH ROW EXECUTE FUNCTION prevent_intake_diagram_version_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат не должен лишить новые осмотры их исторической схемы.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Maintenance"
                               WHERE "IntakeDiagramVersion" <> 'sedan-v1') THEN
                        RAISE EXCEPTION 'Откат невозможен: существуют осмотры с более новой версией схемы.';
                    END IF;
                END $$;
                DROP TRIGGER "TR_Maintenance_IntakeDiagramVersion" ON "Maintenance";
                DROP FUNCTION prevent_intake_diagram_version_change();
                """);

            migrationBuilder.DropTable(
                name: "MaintenanceIntakeDamagePhotos");

            migrationBuilder.DropTable(
                name: "MaintenanceIntakeDamages");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MaintenancePhotos_MaintenanceId_Id",
                table: "MaintenancePhotos");

            migrationBuilder.DropColumn(
                name: "IntakeDiagramVersion",
                table: "Maintenance");
        }
    }
}
