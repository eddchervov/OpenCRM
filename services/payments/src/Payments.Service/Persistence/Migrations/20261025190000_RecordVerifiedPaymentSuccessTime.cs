using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Service.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordVerifiedPaymentSuccessTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SucceededAt",
                schema: "payments",
                table: "PaymentOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE payments.\"PaymentOrders\" SET \"SucceededAt\" = CURRENT_TIMESTAMP WHERE \"Status\" = 'Succeeded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM payments.\"PaymentOrders\" WHERE \"SucceededAt\" IS NOT NULL) THEN RAISE EXCEPTION 'Нельзя удалить время подтверждения принятых финансовых платежей'; END IF; END; $$;");

            migrationBuilder.DropColumn(
                name: "SucceededAt",
                schema: "payments",
                table: "PaymentOrders");
        }
    }
}
