using Microsoft.EntityFrameworkCore.Migrations;

using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260510120000_AddOrganizationSchedulerWorkflowStages")]
    public partial class AddOrganizationSchedulerWorkflowStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessCode",
                table: "OrganizationSchedulerTaskStates",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "service-station");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowStageCode",
                table: "OrganizationSchedulerTaskStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "intake");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTaskStates_OrganizationId_ProcessCode_WorkflowStageCode",
                table: "OrganizationSchedulerTaskStates",
                columns: new[] { "OrganizationId", "ProcessCode", "WorkflowStageCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTaskStates_OrganizationId_ProcessCode_WorkflowStageCode",
                table: "OrganizationSchedulerTaskStates");

            migrationBuilder.DropColumn(
                name: "ProcessCode",
                table: "OrganizationSchedulerTaskStates");

            migrationBuilder.DropColumn(
                name: "WorkflowStageCode",
                table: "OrganizationSchedulerTaskStates");
        }
    }
}
