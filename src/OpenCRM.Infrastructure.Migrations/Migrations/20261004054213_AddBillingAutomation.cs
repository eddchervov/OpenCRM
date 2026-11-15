using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReconciledAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NpdReceiptDeliveryAttempt",
                table: "SubscriptionPayments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptDeliverySourceHash",
                table: "SubscriptionPayments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NpdReceiptEmailMessageId",
                table: "SubscriptionPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "SubscriptionPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_LastReconciledAt",
                table: "SubscriptionPayments",
                column: "LastReconciledAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPayments_LastReconciledAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "LastReconciledAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptDeliveryAttempt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptDeliverySourceHash",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptEmailMessageId",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "SubscriptionPayments");
        }
    }
}
