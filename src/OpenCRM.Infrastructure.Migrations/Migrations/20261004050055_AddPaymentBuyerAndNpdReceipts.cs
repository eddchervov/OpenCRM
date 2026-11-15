using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBuyerAndNpdReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerInn",
                table: "SubscriptionPayments",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "SubscriptionPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerTypeCode",
                table: "SubscriptionPayments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptComment",
                table: "SubscriptionPayments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptFileContentType",
                table: "SubscriptionPayments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptFileName",
                table: "SubscriptionPayments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptFilePath",
                table: "SubscriptionPayments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NpdReceiptIssuedAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptMarkedByUserId",
                table: "SubscriptionPayments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptReason",
                table: "SubscriptionPayments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NpdReceiptUrl",
                table: "SubscriptionPayments",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"SubscriptionPayments\" SET \"PaidAt\" = COALESCE(\"StatusUpdatedAt\", \"CreatedAt\") WHERE \"PaymentStatus\" = 'completed'");

            migrationBuilder.CreateTable(
                name: "NpdReceiptAudits",
                schema: "admin",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionPaymentId = table.Column<int>(type: "integer", nullable: false),
                    Issued = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpdReceiptAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NpdReceiptAudits_SubscriptionPayments_SubscriptionPaymentId",
                        column: x => x.SubscriptionPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NpdReceiptAudits_SubscriptionPaymentId_ChangedAt",
                schema: "admin",
                table: "NpdReceiptAudits",
                columns: new[] { "SubscriptionPaymentId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NpdReceiptAudits",
                schema: "admin");

            migrationBuilder.DropColumn(
                name: "BuyerInn",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "BuyerTypeCode",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptComment",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptFileContentType",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptFileName",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptFilePath",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptIssuedAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptMarkedByUserId",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptReason",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "NpdReceiptUrl",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "SubscriptionPayments");
        }
    }
}
