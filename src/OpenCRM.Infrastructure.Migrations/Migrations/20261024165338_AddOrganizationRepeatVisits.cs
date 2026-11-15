using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationRepeatVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationRepeatVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SuggestedVisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResponsibleUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ContactByUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContactCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    BookedMaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRepeatVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_AspNetUsers_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_Maintenance_BookedMaintenanceId",
                        column: x => x.BookedMaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_Maintenance_SourceMaintenanceId",
                        column: x => x.SourceMaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_OrganizationClients_OrganizationCl~",
                        column: x => x.OrganizationClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationRepeatVisits_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_BookedMaintenanceId",
                table: "OrganizationRepeatVisits",
                column: "BookedMaintenanceId",
                unique: true,
                filter: "\"BookedMaintenanceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_CreatedByUserId",
                table: "OrganizationRepeatVisits",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_OrganizationClientId",
                table: "OrganizationRepeatVisits",
                column: "OrganizationClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_OrganizationId_ResponsibleUserId_C~",
                table: "OrganizationRepeatVisits",
                columns: new[] { "OrganizationId", "ResponsibleUserId", "ContactByUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_OrganizationId_SuggestedVisitDate_~",
                table: "OrganizationRepeatVisits",
                columns: new[] { "OrganizationId", "SuggestedVisitDate", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_ResponsibleUserId",
                table: "OrganizationRepeatVisits",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_SourceMaintenanceId",
                table: "OrganizationRepeatVisits",
                column: "SourceMaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRepeatVisits_VehicleId",
                table: "OrganizationRepeatVisits",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationRepeatVisits");
        }
    }
}
