using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260509150000_AddOrganizationAuditEvents")]
    public partial class AddOrganizationAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    TargetType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAuditEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_ActorUserId",
                table: "OrganizationAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_EventType",
                table: "OrganizationAuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAuditEvents_OrganizationId_CreatedAt",
                table: "OrganizationAuditEvents",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationAuditEvents");
        }
    }
}
