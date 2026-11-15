using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class IndexVisitReminderQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelegramClientNotifications_MaintenanceId_VisitStartsAtUtc",
                table: "TelegramClientNotifications",
                columns: new[] { "MaintenanceId", "VisitStartsAtUtc" },
                filter: "\"EventCode\" = 'booking_reminder'");

            migrationBuilder.CreateIndex(
                name: "IX_MaxClientNotifications_MaintenanceId_VisitStartsAtUtc",
                table: "MaxClientNotifications",
                columns: new[] { "MaintenanceId", "VisitStartsAtUtc" },
                filter: "\"EventCode\" = 'booking_reminder'");

            migrationBuilder.CreateIndex(
                name: "IX_Maintenance_VisitReminders",
                table: "Maintenance",
                columns: new[] { "PlannedDate", "PlannedStartTime", "Id" },
                filter: "\"Status\" = 1 AND NOT \"IsDeleted\" AND \"VisitReminderEnabled\" AND \"PlannedStartTime\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelegramClientNotifications_MaintenanceId_VisitStartsAtUtc",
                table: "TelegramClientNotifications");

            migrationBuilder.DropIndex(
                name: "IX_MaxClientNotifications_MaintenanceId_VisitStartsAtUtc",
                table: "MaxClientNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Maintenance_VisitReminders",
                table: "Maintenance");
        }
    }
}
