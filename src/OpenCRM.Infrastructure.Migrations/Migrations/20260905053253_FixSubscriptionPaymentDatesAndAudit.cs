using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FixSubscriptionPaymentDatesAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaymentDate",
                table: "SubscriptionPayments",
                newName: "CreatedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusUpdatedAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentStatusAudits",
                schema: "admin",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatusAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentStatusAudits_SubscriptionPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentStatusAudits_PaymentId_ChangedAt",
                schema: "admin",
                table: "PaymentStatusAudits",
                columns: new[] { "PaymentId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentStatusAudits",
                schema: "admin");

            migrationBuilder.DropColumn(
                name: "StatusUpdatedAt",
                table: "SubscriptionPayments");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "SubscriptionPayments",
                newName: "PaymentDate");
        }
    }
}
