using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddMaintenanceWorkReportsAndPhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DetailId",
            table: "MaintenancePhotos",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaintenancePhotos_MaintenanceId_DetailId",
            table: "MaintenancePhotos",
            columns: new[] { "MaintenanceId", "DetailId" });

        migrationBuilder.CreateTable(
            name: "MaintenanceWorkReports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                DetailId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                DecidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovedDetailId = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovedPrice = table.Column<double>(type: "double precision", nullable: true),
                TimeAdjustmentMinutes = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaintenanceWorkReports", column => column.Id);
                table.ForeignKey(
                    name: "FK_MaintenanceWorkReports_Maintenance_MaintenanceId",
                    column: column => column.MaintenanceId,
                    principalTable: "Maintenance",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkReports_CommandId",
            table: "MaintenanceWorkReports",
            column: "CommandId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkReports_MaintenanceId_DetailId_CreatedAt",
            table: "MaintenanceWorkReports",
            columns: new[] { "MaintenanceId", "DetailId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkReports_OrganizationId_Kind_Decision",
            table: "MaintenanceWorkReports",
            columns: new[] { "OrganizationId", "Kind", "Decision" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaintenanceWorkReports");
        migrationBuilder.DropIndex(
            name: "IX_MaintenancePhotos_MaintenanceId_DetailId",
            table: "MaintenancePhotos");
        migrationBuilder.DropColumn(name: "DetailId", table: "MaintenancePhotos");
    }
}
