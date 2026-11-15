using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationWeeklyWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationWorkingDays",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationWorkingDays", x => new { x.OrganizationId, x.DayOfWeek });
                    table.CheckConstraint("CK_OrganizationWorkingDays_DayAndTime", "\"DayOfWeek\" >= 0 AND \"DayOfWeek\" <= 6 AND \"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_OrganizationWorkingDays_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationWorkingDayBreaks",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationWorkingDayBreaks", x => new { x.OrganizationId, x.DayOfWeek, x.StartTime });
                    table.CheckConstraint("CK_OrganizationWorkingDayBreaks_Time", "\"StartTime\" < \"EndTime\"");
                    table.ForeignKey(
                        name: "FK_OrganizationWorkingDayBreaks_OrganizationWorkingDays_Organi~",
                        columns: x => new { x.OrganizationId, x.DayOfWeek },
                        principalTable: "OrganizationWorkingDays",
                        principalColumns: new[] { "OrganizationId", "DayOfWeek" },
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Organizations" AS organization
                        WHERE cardinality(organization."WorkingDays") > 0
                          AND (organization."WorkdayStart" IS NULL OR organization."WorkdayEnd" IS NULL
                            OR organization."WorkdayStart" >= organization."WorkdayEnd"
                            OR EXISTS (SELECT 1 FROM unnest(organization."WorkingDays") AS day(value)
                                WHERE day.value IS NULL OR day.value < 0 OR day.value > 6))
                    ) THEN
                        RAISE EXCEPTION 'Перед переносом расписания исправьте рабочие дни и часы организаций.';
                    END IF;
                END $$;

                INSERT INTO "OrganizationWorkingDays" ("OrganizationId", "DayOfWeek", "StartTime", "EndTime")
                SELECT DISTINCT organization."Id", day.value, organization."WorkdayStart", organization."WorkdayEnd"
                FROM "Organizations" AS organization
                CROSS JOIN LATERAL unnest(organization."WorkingDays") AS day(value);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationWorkingDayBreaks")
                        OR EXISTS (
                            SELECT 1 FROM "OrganizationWorkingDays"
                            GROUP BY "OrganizationId"
                            HAVING count(DISTINCT ("StartTime", "EndTime")) > 1
                        ) THEN
                        RAISE EXCEPTION 'Откат потеряет отдельные часы или перерывы. Сначала сохраните полное расписание и определите общий график.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "OrganizationWorkingDayBreaks");

            migrationBuilder.DropTable(
                name: "OrganizationWorkingDays");
        }
    }
}
