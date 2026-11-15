using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CaptureMaintenanceBuyerAndPaymentPayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayerClientId",
                table: "MaintenancePayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerInn",
                table: "MaintenancePayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerKpp",
                table: "MaintenancePayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerLegalAddress",
                table: "MaintenancePayments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerName",
                table: "MaintenancePayments",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayerPartyTypeCode",
                table: "MaintenancePayments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BuyerClientId",
                table: "Maintenance",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerInn",
                table: "Maintenance",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerKpp",
                table: "Maintenance",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerLegalAddress",
                table: "Maintenance",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "Maintenance",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPartyTypeCode",
                table: "Maintenance",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePayments_PayerClientId",
                table: "MaintenancePayments",
                column: "PayerClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Maintenance_BuyerClientId",
                table: "Maintenance",
                column: "BuyerClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Maintenance_OrganizationClients_BuyerClientId",
                table: "Maintenance",
                column: "BuyerClientId",
                principalTable: "OrganizationClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenancePayments_OrganizationClients_PayerClientId",
                table: "MaintenancePayments",
                column: "PayerClientId",
                principalTable: "OrganizationClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Maintenance_OrganizationClients_BuyerClientId",
                table: "Maintenance");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenancePayments_OrganizationClients_PayerClientId",
                table: "MaintenancePayments");

            migrationBuilder.DropIndex(
                name: "IX_MaintenancePayments_PayerClientId",
                table: "MaintenancePayments");

            migrationBuilder.DropIndex(
                name: "IX_Maintenance_BuyerClientId",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "PayerClientId",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "PayerInn",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "PayerKpp",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "PayerLegalAddress",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "PayerName",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "PayerPartyTypeCode",
                table: "MaintenancePayments");

            migrationBuilder.DropColumn(
                name: "BuyerClientId",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "BuyerInn",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "BuyerKpp",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "BuyerLegalAddress",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "Maintenance");

            migrationBuilder.DropColumn(
                name: "BuyerPartyTypeCode",
                table: "Maintenance");
        }
    }
}
