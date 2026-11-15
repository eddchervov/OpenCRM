using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Infrastructure.Data;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenCRMContext))]
    [Migration("20260511170000_AddSchedulerRecurringTaskInstances")]
    public partial class AddSchedulerRecurringTaskInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceGeneratedUntil",
                table: "OrganizationSchedulerTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceParentTaskId",
                table: "OrganizationSchedulerTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_RecurrenceCode_Rec~",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "RecurrenceCode", "RecurrenceGeneratedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_RecurrenceParentT~",
                table: "OrganizationSchedulerTasks",
                columns: new[] { "OrganizationId", "RecurrenceParentTaskId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSchedulerTasks_RecurrenceParentTaskId",
                table: "OrganizationSchedulerTasks",
                column: "RecurrenceParentTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationSchedulerTasks_OrganizationSchedulerTasks_Recur~",
                table: "OrganizationSchedulerTasks",
                column: "RecurrenceParentTaskId",
                principalTable: "OrganizationSchedulerTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationSchedulerTasks_OrganizationSchedulerTasks_Recur~",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_RecurrenceCode_Rec~",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_OrganizationId_RecurrenceParentT~",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSchedulerTasks_RecurrenceParentTaskId",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceGeneratedUntil",
                table: "OrganizationSchedulerTasks");

            migrationBuilder.DropColumn(
                name: "RecurrenceParentTaskId",
                table: "OrganizationSchedulerTasks");
        }
    }
}
