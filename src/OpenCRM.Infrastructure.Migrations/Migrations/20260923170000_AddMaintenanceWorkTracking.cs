using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations;

public partial class AddMaintenanceWorkTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaintenanceWorkOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaintenanceWorkOperations", column => column.Id);
                table.ForeignKey(
                    name: "FK_MaintenanceWorkOperations_Maintenance_MaintenanceId",
                    column: column => column.MaintenanceId,
                    principalTable: "Maintenance",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MaintenanceWorkEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                PreviousVersion = table.Column<uint>(type: "bigint", nullable: false),
                ResultVersion = table.Column<uint>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaintenanceWorkEvents", column => column.Id);
                table.ForeignKey(
                    name: "FK_MaintenanceWorkEvents_MaintenanceWorkOperations_OperationId",
                    column: column => column.OperationId,
                    principalTable: "MaintenanceWorkOperations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MaintenanceWorkIntervals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaintenanceWorkIntervals", column => column.Id);
                table.ForeignKey(
                    name: "FK_MaintenanceWorkIntervals_MaintenanceWorkOperations_OperationId",
                    column: column => column.OperationId,
                    principalTable: "MaintenanceWorkOperations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkOperations_MaintenanceId",
            table: "MaintenanceWorkOperations",
            column: "MaintenanceId");
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkOperations_OrganizationId_EmployeeId_Status",
            table: "MaintenanceWorkOperations",
            columns: new[] { "OrganizationId", "EmployeeId", "Status" });
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkEvents_CommandId",
            table: "MaintenanceWorkEvents",
            column: "CommandId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkEvents_OperationId_CreatedAt",
            table: "MaintenanceWorkEvents",
            columns: new[] { "OperationId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_MaintenanceWorkIntervals_OperationId",
            table: "MaintenanceWorkIntervals",
            column: "OperationId",
            unique: true,
            filter: "\"EndedAt\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaintenanceWorkEvents");
        migrationBuilder.DropTable(name: "MaintenanceWorkIntervals");
        migrationBuilder.DropTable(name: "MaintenanceWorkOperations");
    }
}
