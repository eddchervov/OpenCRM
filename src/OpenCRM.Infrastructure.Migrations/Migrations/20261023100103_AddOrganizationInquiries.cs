using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationInquiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationInquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    NormalizedPhone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SourceCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Interest = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    OrganizationClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StageCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponsibleUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    NextActionText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NextActionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CloseReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_AspNetUsers_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_OrganizationClients_OrganizationClien~",
                        column: x => x.OrganizationClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiries_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationInquiryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationInquiryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreviousValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInquiryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiryEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationInquiryEvents_OrganizationInquiries_Organizatio~",
                        column: x => x.OrganizationInquiryId,
                        principalTable: "OrganizationInquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_CreatedByUserId",
                table: "OrganizationInquiries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_MaintenanceId",
                table: "OrganizationInquiries",
                column: "MaintenanceId",
                unique: true,
                filter: "\"MaintenanceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_OrganizationClientId",
                table: "OrganizationInquiries",
                column: "OrganizationClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_OrganizationId_NormalizedPhone",
                table: "OrganizationInquiries",
                columns: new[] { "OrganizationId", "NormalizedPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_OrganizationId_ResponsibleUserId_Next~",
                table: "OrganizationInquiries",
                columns: new[] { "OrganizationId", "ResponsibleUserId", "NextActionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_OrganizationId_StageCode_UpdatedAt",
                table: "OrganizationInquiries",
                columns: new[] { "OrganizationId", "StageCode", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_ResponsibleUserId",
                table: "OrganizationInquiries",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiries_VehicleId",
                table: "OrganizationInquiries",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiryEvents_ActorUserId",
                table: "OrganizationInquiryEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiryEvents_OrganizationId_CreatedAt",
                table: "OrganizationInquiryEvents",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInquiryEvents_OrganizationInquiryId_CreatedAt",
                table: "OrganizationInquiryEvents",
                columns: new[] { "OrganizationInquiryId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "OrganizationInquiries" IN ACCESS EXCLUSIVE MODE;
                DO $inquiry_rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "OrganizationInquiries") THEN
                        RAISE EXCEPTION 'Откат миграции обращений невозможен без потери обращений и их истории. Сначала сохраните данные и выполните согласованное восстановление.';
                    END IF;
                END $inquiry_rollback$;
                """);

            migrationBuilder.DropTable(
                name: "OrganizationInquiryEvents");

            migrationBuilder.DropTable(
                name: "OrganizationInquiries");
        }
    }
}
