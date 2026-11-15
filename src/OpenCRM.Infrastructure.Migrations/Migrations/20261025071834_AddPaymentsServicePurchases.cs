using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsServicePurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPurchaseId",
                table: "UserSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProviderCode",
                table: "SubscriptionPayments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "yookassa");

            migrationBuilder.AddColumn<string>(
                name: "PaymentProviderCode",
                table: "PaymentCreationOperations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "yookassa");

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPurchaseId",
                table: "OrganizationSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    CreationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPaymentId = table.Column<int>(type: "integer", nullable: false),
                    PlanSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProviderOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RefundedAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    ApplicationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApplicationAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextApplicationAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextReconcileAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPurchases", x => x.Id);
                    table.CheckConstraint("CK_SubscriptionPurchases_Amounts", "\"AmountMinor\" > 0 AND \"RefundedAmountMinor\" >= 0 AND \"RefundedAmountMinor\" <= \"AmountMinor\"");
                    table.CheckConstraint("CK_SubscriptionPurchases_Version", "\"ProviderVersion\" >= 0");
                    table.ForeignKey(
                        name: "FK_SubscriptionPurchases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPurchases_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPurchases_PaymentCreationOperations_CreationOpe~",
                        column: x => x.CreationOperationId,
                        principalTable: "PaymentCreationOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPurchases_SubscriptionPayments_SubscriptionPaym~",
                        column: x => x.SubscriptionPaymentId,
                        principalTable: "SubscriptionPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPaymentCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPaymentCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPaymentCommands_SubscriptionPurchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "SubscriptionPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderVersion = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MessageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    RefundedAmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPaymentEvents_SubscriptionPurchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "SubscriptionPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_SubscriptionPurchaseId",
                table: "UserSubscriptions",
                column: "SubscriptionPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_SubscriptionPurchaseId",
                table: "OrganizationSubscriptions",
                column: "SubscriptionPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentCommands_PurchaseId",
                table: "SubscriptionPaymentCommands",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentCommands_SentAt_NextAttemptAt",
                table: "SubscriptionPaymentCommands",
                columns: new[] { "SentAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentEvents_MessageId",
                table: "SubscriptionPaymentEvents",
                column: "MessageId",
                unique: true,
                filter: "\"MessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentEvents_ProcessedAt_NextAttemptAt",
                table: "SubscriptionPaymentEvents",
                columns: new[] { "ProcessedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPaymentEvents_PurchaseId_CreatedAt",
                table: "SubscriptionPaymentEvents",
                columns: new[] { "PurchaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_ApplicationStatus_NextApplicationAtte~",
                table: "SubscriptionPurchases",
                columns: new[] { "ApplicationStatus", "NextApplicationAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_BeneficiaryKey",
                table: "SubscriptionPurchases",
                column: "BeneficiaryKey",
                unique: true,
                filter: "\"IsOpen\"");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_CreationOperationId",
                table: "SubscriptionPurchases",
                column: "CreationOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_NextReconcileAt",
                table: "SubscriptionPurchases",
                column: "NextReconcileAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_OrganizationId",
                table: "SubscriptionPurchases",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_ProviderOrderId",
                table: "SubscriptionPurchases",
                column: "ProviderOrderId",
                unique: true,
                filter: "\"ProviderOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_SubscriptionPaymentId",
                table: "SubscriptionPurchases",
                column: "SubscriptionPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_UserId",
                table: "SubscriptionPurchases",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationSubscriptions_SubscriptionPurchases_Subscriptio~",
                table: "OrganizationSubscriptions",
                column: "SubscriptionPurchaseId",
                principalTable: "SubscriptionPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPurchases_SubscriptionPurchas~",
                table: "UserSubscriptions",
                column: "SubscriptionPurchaseId",
                principalTable: "SubscriptionPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION "GuardSubscriptionPurchaseSnapshot"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(NEW."Id", NEW."UserId", NEW."OrganizationId", NEW."BeneficiaryKey", NEW."CreationOperationId",
                           NEW."SubscriptionPaymentId", NEW."PlanSnapshot", NEW."AmountMinor", NEW."Currency", NEW."IsTest")
                       IS DISTINCT FROM
                       ROW(OLD."Id", OLD."UserId", OLD."OrganizationId", OLD."BeneficiaryKey", OLD."CreationOperationId",
                           OLD."SubscriptionPaymentId", OLD."PlanSnapshot", OLD."AmountMinor", OLD."Currency", OLD."IsTest") THEN
                        RAISE EXCEPTION 'Условия принятой финансовой покупки неизменяемы';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "SubscriptionPurchaseSnapshotImmutable" BEFORE UPDATE ON "SubscriptionPurchases"
                    FOR EACH ROW EXECUTE FUNCTION "GuardSubscriptionPurchaseSnapshot"();
                CREATE FUNCTION "GuardSubscriptionPaymentEvent"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Удаление финансового журнала запрещено';
                    END IF;
                    IF OLD."MessageId" IS NULL
                        OR ROW(NEW."Id", NEW."MessageId", NEW."Kind", NEW."Payload", NEW."MessageHash", NEW."CreatedAt")
                           IS DISTINCT FROM ROW(OLD."Id", OLD."MessageId", OLD."Kind", OLD."Payload", OLD."MessageHash", OLD."CreatedAt") THEN
                        RAISE EXCEPTION 'Содержание финансового события неизменяемо';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "SubscriptionPaymentEventImmutable" BEFORE UPDATE OR DELETE ON "SubscriptionPaymentEvents"
                    FOR EACH ROW EXECUTE FUNCTION "GuardSubscriptionPaymentEvent"();
                CREATE FUNCTION "GuardSubscriptionPaymentCommand"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF ROW(NEW."Id", NEW."PurchaseId", NEW."Kind", NEW."Payload", NEW."PayloadHash", NEW."CreatedAt")
                       IS DISTINCT FROM ROW(OLD."Id", OLD."PurchaseId", OLD."Kind", OLD."Payload", OLD."PayloadHash", OLD."CreatedAt") THEN
                        RAISE EXCEPTION 'Принятое финансовое поручение неизменяемо';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "SubscriptionPaymentCommandImmutable" BEFORE UPDATE ON "SubscriptionPaymentCommands"
                    FOR EACH ROW EXECUTE FUNCTION "GuardSubscriptionPaymentCommand"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "SubscriptionPurchases")
                        OR EXISTS (SELECT 1 FROM "SubscriptionPaymentCommands")
                        OR EXISTS (SELECT 1 FROM "SubscriptionPaymentEvents") THEN
                        RAISE EXCEPTION 'Откат финансовой схемы с данными запрещён: сначала требуются резервирование и сверка';
                    END IF;
                END $$;
                DROP FUNCTION "GuardSubscriptionPurchaseSnapshot"() CASCADE;
                DROP FUNCTION "GuardSubscriptionPaymentCommand"() CASCADE;
                DROP FUNCTION "GuardSubscriptionPaymentEvent"() CASCADE;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationSubscriptions_SubscriptionPurchases_Subscriptio~",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserSubscriptions_SubscriptionPurchases_SubscriptionPurchas~",
                table: "UserSubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPaymentCommands");

            migrationBuilder.DropTable(
                name: "SubscriptionPaymentEvents");

            migrationBuilder.DropTable(
                name: "SubscriptionPurchases");

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_SubscriptionPurchaseId",
                table: "UserSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationSubscriptions_SubscriptionPurchaseId",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionPurchaseId",
                table: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentProviderCode",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "PaymentProviderCode",
                table: "PaymentCreationOperations");

            migrationBuilder.DropColumn(
                name: "SubscriptionPurchaseId",
                table: "OrganizationSubscriptions");
        }
    }
}
