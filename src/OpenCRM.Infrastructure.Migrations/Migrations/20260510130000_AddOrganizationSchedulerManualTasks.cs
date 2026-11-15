using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260510130000_AddOrganizationSchedulerManualTasks")]
    public partial class AddOrganizationSchedulerManualTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationSchedulerTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationClientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsibleUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TypeCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PriorityCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProcessCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    WorkflowStageCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSchedulerTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTasks_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTasks_AspNetUsers_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTasks_OrganizationClients_OrganizationClientId",
                        column: x => x.OrganizationClientId,
                        principalTable: "OrganizationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTasks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationSchedulerTasks_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_CreatedByUserId",
                table: "OrganizationSchedulerTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationClientId",
                table: "OrganizationSchedulerTasks",
                column: "OrganizationClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_IsDeleted_StartAt",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "IsDeleted", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_OrganizationClientId",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "OrganizationClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_PriorityCode",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "PriorityCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_ResponsibleUserId",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "ResponsibleUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_StatusCode",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "StatusCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_TypeCode",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "TypeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_VehicleId",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_ResponsibleUserId",
                table: "OrganizationSchedulerTasks",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_VehicleId",
                table: "OrganizationSchedulerTasks",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationSchedulerTasks");
        }
    }
}
