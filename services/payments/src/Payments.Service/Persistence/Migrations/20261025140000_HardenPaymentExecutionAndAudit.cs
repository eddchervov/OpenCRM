using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Service.Persistence.Migrations;

public partial class HardenPaymentExecutionAndAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExecutionMode",
            schema: "payments",
            table: "ProviderOperations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Disabled");

        migrationBuilder.AddColumn<DateTimeOffset?>(
            name: "UpdatedAt",
            schema: "payments",
            table: "ProviderOperations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE payments.\"ProviderOperations\" SET \"UpdatedAt\" = \"CreatedAt\"");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "UpdatedAt",
            schema: "payments",
            table: "ProviderOperations",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset?),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReconciliationFailureCount",
            schema: "payments",
            table: "PaymentOrders",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "PaymentAuditEntries",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                ProviderOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ErrorType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                RefundedAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentAuditEntries", entry => entry.Id);
                table.ForeignKey(name: "FK_PaymentAuditEntries_PaymentOrders_PaymentOrderId", column: entry => entry.PaymentOrderId,
                    principalSchema: "payments", principalTable: "PaymentOrders", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "FK_PaymentAuditEntries_ProviderOperations_ProviderOperationId", column: entry => entry.ProviderOperationId,
                    principalSchema: "payments", principalTable: "ProviderOperations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentAuditEntries_PaymentOrderId_OccurredAt",
            schema: "payments", table: "PaymentAuditEntries", columns: new[] { "PaymentOrderId", "OccurredAt" });
        migrationBuilder.CreateIndex(
            name: "IX_PaymentAuditEntries_ProviderOperationId",
            schema: "payments", table: "PaymentAuditEntries", column: "ProviderOperationId");

        migrationBuilder.Sql("UPDATE payments.\"ProviderOperations\" SET \"Status\" = 'ManualReview', \"LastError\" = 'execution_mode_not_recorded', \"NextAttemptAt\" = 'infinity' WHERE \"Status\" IN ('Prepared', 'Sending', 'OutcomeUnknown', 'AwaitingProvider')");
        migrationBuilder.Sql("CREATE FUNCTION payments.prevent_payment_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'PaymentAuditEntries are append-only'; END; $$");
        migrationBuilder.Sql("CREATE TRIGGER TR_PaymentAuditEntries_AppendOnly BEFORE UPDATE OR DELETE ON payments.\"PaymentAuditEntries\" FOR EACH ROW EXECUTE FUNCTION payments.prevent_payment_audit_mutation()");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM payments.\"PaymentAuditEntries\") OR EXISTS (SELECT 1 FROM payments.\"ProviderOperations\" WHERE \"ExecutionMode\" = 'Disabled') THEN RAISE EXCEPTION 'Cannot roll back payment operations without a recorded execution mode'; END IF; END; $$;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_PaymentAuditEntries_AppendOnly ON payments.\"PaymentAuditEntries\"");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS payments.prevent_payment_audit_mutation()");
        migrationBuilder.DropTable(name: "PaymentAuditEntries", schema: "payments");
        migrationBuilder.DropColumn(name: "UpdatedAt", schema: "payments", table: "ProviderOperations");
        migrationBuilder.DropColumn(name: "ExecutionMode", schema: "payments", table: "ProviderOperations");
        migrationBuilder.DropColumn(name: "ReconciliationFailureCount", schema: "payments", table: "PaymentOrders");
    }
}
