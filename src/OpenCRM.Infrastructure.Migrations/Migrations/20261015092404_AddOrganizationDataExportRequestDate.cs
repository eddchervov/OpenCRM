using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationDataExportRequestDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastDataExportRequestedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: true);

            // Сохраняем действующие ограничения для организаций, которые уже запрашивали выгрузку.
            migrationBuilder.Sql("""
                UPDATE "Organizations" AS organization
                SET "LastDataExportRequestedAt" = history."LastRequestedAt"
                FROM (
                    SELECT "OrganizationId", MAX("CreatedAt") AS "LastRequestedAt"
                    FROM "OrganizationAuditEvents"
                    WHERE "EventType" = 'organization.data-export.requested'
                    GROUP BY "OrganizationId"
                ) AS history
                WHERE organization."Id" = history."OrganizationId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Возвращаем дату в журнал, чтобы предыдущая версия приложения не потеряла суточное ограничение.
            migrationBuilder.Sql("""
                INSERT INTO "OrganizationAuditEvents"
                    ("Id", "OrganizationId", "ActorUserId", "EventType", "Title", "CreatedAt")
                SELECT gen_random_uuid(), organization."Id", organization."OwnerUserId",
                    'organization.data-export.requested', 'Запрошена выгрузка данных организации',
                    organization."LastDataExportRequestedAt"
                FROM "Organizations" AS organization
                WHERE organization."LastDataExportRequestedAt" IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM "OrganizationAuditEvents" AS history
                        WHERE history."OrganizationId" = organization."Id"
                            AND history."EventType" = 'organization.data-export.requested'
                            AND history."CreatedAt" >= organization."LastDataExportRequestedAt"
                    );
                """);

            migrationBuilder.DropColumn(
                name: "LastDataExportRequestedAt",
                table: "Organizations");
        }
    }
}
