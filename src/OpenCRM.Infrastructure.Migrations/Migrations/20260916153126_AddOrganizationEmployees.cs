using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "MaintenanceHistoryEvents"
                    ("Id", "MaintenanceId", "VehicleId", "OrganizationId", "UserId", "ActorDisplayName", "EventType", "Title", "Description", "PayloadJson", "CreatedAt")
                SELECT gen_random_uuid(), m."Id", m."VehicleId", v."OrganizationId", m."UserId",
                    'Системное обновление', 'manual_performer_cleared', 'Переход на справочник сотрудников',
                    'Снято ручное назначение: ' || m."Performer" || '. Выберите сотрудника из справочника.',
                    jsonb_build_object('previousPerformer', m."Performer"), now()
                FROM "Maintenance" m JOIN "Vehicles" v ON v."Id" = m."VehicleId"
                WHERE v."OrganizationId" IS NOT NULL AND m."Performer" IS NOT NULL;

                UPDATE "Maintenance" m SET "Performer" = NULL
                FROM "Vehicles" v WHERE v."Id" = m."VehicleId" AND v."OrganizationId" IS NOT NULL;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "Maintenance",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationEmployees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationEmployees_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Maintenance_EmployeeId",
                table: "Maintenance",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationEmployees_OrganizationId_IsActive",
                table: "OrganizationEmployees",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Maintenance_OrganizationEmployees_EmployeeId",
                table: "Maintenance",
                column: "EmployeeId",
                principalTable: "OrganizationEmployees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "OrganizationEmployees", "Maintenance" IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationEmployees") THEN
                        RAISE EXCEPTION 'Откат остановлен: справочник сотрудников уже заполнен. Сначала сохраните данные.';
                    END IF;
                END $$;
                UPDATE "Maintenance" m SET "Performer" = h."PayloadJson"::jsonb ->> 'previousPerformer'
                FROM "MaintenanceHistoryEvents" h
                WHERE h."MaintenanceId" = m."Id" AND h."EventType" = 'manual_performer_cleared'
                    AND m."Performer" IS NULL AND m."EmployeeId" IS NULL;
                DELETE FROM "MaintenanceHistoryEvents" WHERE "EventType" = 'manual_performer_cleared';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Maintenance_OrganizationEmployees_EmployeeId",
                table: "Maintenance");

            migrationBuilder.DropTable(
                name: "OrganizationEmployees");

            migrationBuilder.DropIndex(
                name: "IX_Maintenance_EmployeeId",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "Maintenance");
        }
    }
}
