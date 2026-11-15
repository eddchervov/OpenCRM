using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenancePaymentsAndServiceBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "ServiceIds",
                table: "OrganizationScenarioCatalogItems",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::uuid[]");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "OrganizationScenarioCatalogItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Maintenance",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "MaintenancePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsOpeningBalance = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePayments", x => x.Id);
                    table.CheckConstraint("CK_MaintenancePayment_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_MaintenancePayments_Maintenance_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "Maintenance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "MaintenancePayments" ("Id", "MaintenanceId", "Amount", "CreatedAt", "CreatedBy", "IsOpeningBalance", "Comment")
                SELECT gen_random_uuid(), m."Id", m."PaidAmount", CURRENT_TIMESTAMP, 'Перенос ранее учтённых оплат', TRUE,
                    'Сумма оплаты, сохранённая до появления истории платежей.'
                FROM "Maintenance" m
                JOIN "Vehicles" v ON v."Id" = m."VehicleId"
                WHERE v."OrganizationId" IS NOT NULL AND m."PaidAmount" > 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePayments_MaintenanceId_CreatedAt",
                table: "MaintenancePayments",
                columns: new[] { "MaintenanceId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "ServiceIds",
                table: "OrganizationScenarioCatalogItems");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "OrganizationScenarioCatalogItems");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Maintenance");
        }
    }
}
