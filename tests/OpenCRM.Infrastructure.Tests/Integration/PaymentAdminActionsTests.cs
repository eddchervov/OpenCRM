using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Testing;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentAdminActionsTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>, IAsyncLifetime
{
    private readonly TestClock _clock = new();
    private readonly PaymentsServiceOptions _options = new()
    {
        Enabled = true, MerchantAccountId = "test-admin-shop", ExpectedIsTest = true,
        BaseUrl = "https://payments.example.test/", ServiceKey = "test-admin-service-key-never-log-00000000000",
        ReturnUrl = "https://app.example.test/payment/complete"
    };

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RefundIsDurableAndExactReplayKeepsBodyAndCommandAfterStateChanged()
    {
        var paymentId = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(paymentId))!;
        var request = new AdminPaymentServiceActionRequest(Guid.NewGuid(), "refund", "Частичный возврат", 150m, state.StateToken);
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(paymentId, request, "admin-a")).StatusCode);
        var original = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync(item => item.Kind == "refund");
        var payload = JsonSerializer.Deserialize<RequestRefund>(original.Payload)!;
        Assert.Equal(15000, payload.AmountMinor);
        Assert.Equal(request.OperationId, payload.CommandId);
        Assert.Equal("admin-a", payload.RequestedBy);
        Assert.Equal(this._options.MerchantAccountId, payload.MerchantAccountId);
        Assert.Equal(TimeSpan.FromHours(23), payload.NotAfter - payload.IssuedAt);
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.LastErrorCode, "broker_delivery_unknown"));
        this._clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(paymentId, request, "admin-a")).StatusCode);
        var replay = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync(item => item.Kind == "refund");
        Assert.Equal(original.Payload, replay.Payload);
        Assert.Equal(original.PayloadHash, replay.PayloadHash);
        Assert.Single(await context.SubscriptionPaymentAdminActions.Where(item => item.Action == "refund").ToListAsync());
        var conflict = await this.Service(context).ExecuteAsync(paymentId, request with { Amount = 151 }, "admin-a");
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("operation_id_conflict", conflict.Code);
        Assert.Equal(409, (await this.Service(context).ExecuteAsync(paymentId, request, "admin-b")).StatusCode);
        Assert.Equal(409, (await this.Service(context).ExecuteAsync(paymentId + 1, request, "admin-a")).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(420.01)]
    public async Task InvalidOrOversizedRefundDoesNotCreateCommand(decimal amount)
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(id))!;
        var result = await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "refund", "Проверка суммы", amount, state.StateToken), "admin");
        Assert.True(result.StatusCode >= 400);
        Assert.Empty(await context.SubscriptionPaymentCommands.Where(item => item.Kind == "refund").ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRefundsReserveThePurchaseAndOnlyOneIsAccepted()
    {
        var id = await this.SeedAsync();
        await using var read = fixture.CreateContext();
        var token = (await this.Service(read).GetAsync(id))!.StateToken;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var context = fixture.CreateContext();
            return await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "refund", "Одновременный возврат", 300, token), "admin");
        }));
        Assert.Single(results, item => item.StatusCode == 202);
        Assert.Single(results, item => item.StatusCode == 409);
        Assert.Single(await read.SubscriptionPaymentCommands.Where(item => item.Kind == "refund").ToListAsync());
        Assert.False((await this.Service(read).GetAsync(id))!.CanRefund);
    }

    [Fact]
    public async Task FreshCheckRequiredAndStaleStateRejected()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(id))!;
        Assert.False(state.CanRefund);
        Assert.False(state.CanRetryAccess);
        Assert.Contains("Проверить платёж", state.RefundHint);
        Assert.Equal(409, (await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "refund", "Возврат", 1, state.StateToken), "admin")).StatusCode);
        await this.FreshAsync(context);
        state = (await this.Service(context).GetAsync(id))!;
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.ProviderVersion, 2));
        Assert.Equal(409, (await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "refund", "Возврат", 1, state.StateToken), "admin")).StatusCode);
    }

    [Theory]
    [InlineData("beneficiary_organization_unavailable", true)]
    [InlineData("subscription_changed_after_checkout", true)]
    [InlineData("paid_subscription_changed_after_checkout", true)]
    [InlineData("financial_snapshot_mismatch", false)]
    public async Task AccessBusinessConflictCanBeRefundedButCannotRetryAccess(string code, bool refundAllowed)
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.ApplicationStatus, SubscriptionPurchaseStatuses.NeedsAttention)
            .SetProperty(item => item.LastErrorCode, code));
        await this.FreshAsync(context);
        var state = (await this.Service(context).GetAsync(id))!;
        Assert.Equal(refundAllowed, state.CanRefund);
        Assert.False(state.CanRetryAccess);
    }

    [Fact]
    public async Task ReviewAcknowledgementKeepsFactsAndReopensOnNewConflictButNotTimestampOnlyChange()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.ApplicationStatus, SubscriptionPurchaseStatuses.NeedsAttention)
            .SetProperty(item => item.LastErrorCode, "financial_snapshot_mismatch"));
        var state = (await this.Service(context).GetAsync(id))!;
        var request = new AdminPaymentServiceActionRequest(Guid.NewGuid(), "resolve-review", "Проверено, передано специалисту", null, state.StateToken);
        Assert.Equal(200, (await this.Service(context).ExecuteAsync(id, request, "admin")).StatusCode);
        Assert.True((await this.Service(context).GetAsync(id))!.ReviewResolved);
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.UpdatedAt, this._clock.GetUtcNow().UtcDateTime.AddMinutes(1)));
        Assert.True((await this.Service(context).GetAsync(id))!.ReviewResolved);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal("Succeeded", purchase.ProviderStatus);
        Assert.Null(purchase.AppliedAt);
        Assert.Equal(0, purchase.RefundedAmountMinor);
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "attention", "financial_snapshot_mismatch", this._clock.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync();
        Assert.False((await this.Service(context).GetAsync(id))!.ReviewResolved);
    }

    [Fact]
    public async Task ResolvedRefundOperationBeforeMoneySnapshotRetainsReservationAndBlocksAutomaticAccess()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(id))!;
        var request = new AdminPaymentServiceActionRequest(Guid.NewGuid(), "refund", "Возврат до выдачи", 100, state.StateToken);
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(id, request, "admin")).StatusCode);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        context.SubscriptionPaymentEvents.Add(new SubscriptionPaymentEvent
        {
            PurchaseId = purchase.Id, CommandId = request.OperationId, Kind = "refund_operation_resolved", Code = "refund_resolved",
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, ProcessedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.False((await this.Service(context).GetAsync(id))!.CanRefund);
        Assert.Null((await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == request.OperationId)).CompletedAt);
        await this.Applicator(context).ApplyAsync(purchase.Id);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.RefundedAmountMinor, 10000));
        this._clock.Advance(TimeSpan.FromMinutes(1));
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.Equal("succeeded", (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == request.OperationId)).Status);
        Assert.False((await this.Service(context).GetAsync(id))!.CanRefund);
    }

    [Fact]
    public async Task UnknownRefundAndReviewAcknowledgementDoNotUnlockAnotherRefund()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "refund", "Возврат", 100, token), "admin");
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        context.SubscriptionPaymentEvents.Add(new SubscriptionPaymentEvent
        {
            PurchaseId = purchase.Id, CommandId = operationId, Kind = "refund_operation_manualreview", Code = "provider_outcome_unknown",
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, ProcessedAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.Equal("needs_attention", (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == operationId)).Status);
        await context.SubscriptionPurchases.ExecuteUpdateAsync(update => update.SetProperty(item => item.ApplicationStatus, SubscriptionPurchaseStatuses.NeedsAttention)
            .SetProperty(item => item.LastErrorCode, "provider_outcome_unknown"));
        token = (await this.Service(context).GetAsync(id))!.StateToken;
        await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "resolve-review", "Разбор принят", null, token), "admin");
        Assert.False((await this.Service(context).GetAsync(id))!.CanRefund);
    }

    [Fact]
    public async Task AccessRetryGrantsExactlyOnceAndDoesNotCreateAnotherPayment()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var request = new AdminPaymentServiceActionRequest(Guid.NewGuid(), "retry-access", "Повтор технической выдачи", null, token);
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(id, request, "admin")).StatusCode);
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.Equal(200, (await this.Service(context).ExecuteAsync(id, request, "admin")).StatusCode);
        this._clock.Advance(TimeSpan.FromMinutes(1));
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.Single(await context.UserSubscriptions.ToListAsync());
        Assert.Single(await context.SubscriptionPayments.ToListAsync());
        Assert.Single(await context.SubscriptionPaymentCommands.ToListAsync());
        Assert.Equal("succeeded", (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == request.OperationId)).Status);
    }

    [Fact]
    public async Task UnavailableServiceLeavesDurableCheckAndNeverMarksItSuccessful()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var request = new AdminPaymentServiceActionRequest(Guid.NewGuid(), "check", "Проверка оплаты", null, token);
        await this.Service(context).ExecuteAsync(id, request, "admin");
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        var action = await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("needs_attention", action.Status);
        Assert.Null(action.CompletedAt);
        Assert.Equal("service_unavailable", action.Code);
        Assert.DoesNotContain("secret", (await this.Service(context).GetAsync(id))!.Operations.Single().Message);
        Assert.False((await this.Service(context).GetAsync(id))!.CanCheck);
    }

    [Fact]
    public async Task MigrationMatchesModelAndRefusesToEraseAdministrativeAudit()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        await this.Service(context).ExecuteAsync(id, new(Guid.NewGuid(), "check", "Сохранённый аудит", null, token), "admin");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync("20261025071834_AddPaymentsServicePurchases"));
        Assert.Contains("Нельзя удалить журнал", error.ToString());
    }

    [Fact]
    public async Task CheckRemainsRequestedUntilNewProviderReadAndThenEnablesFinancialActions()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        var state = (await this.Service(context).GetAsync(id))!;
        var operationId = Guid.NewGuid();
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(id,
            new(operationId, "check", "Проверить фактическую оплату", null, state.StateToken), "admin")).StatusCode);
        var providerChecked = false;
        var handler = new SnapshotHandler(() => new
        {
            Id = purchase.ProviderOrderId, ApplicationId = "opencrm", PurchaseId = purchase.Id, Version = purchase.ProviderVersion,
            purchase.AmountMinor, purchase.Currency, PaymentStatus = purchase.ProviderStatus, purchase.RefundedAmountMinor,
            ProviderPaymentId = purchase.Payment.ExternalPaymentId, ConfirmationUrl = (string?)null, IsTest = true,
            LastReconciledAt = providerChecked ? (DateTimeOffset?)this._clock.GetUtcNow() : null,
            LastError = (string?)null,
            Operations = new[] { new { Id = Guid.NewGuid(), CommandId = purchase.Id, UpdatedAt = this._clock.GetUtcNow(),
                AttemptCount = 1, Kind = "CreatePayment", Status = "Resolved", LastError = (string?)null } }
        }, purchase.Id, this._clock);
        await this.Worker(context, handler).ProcessDueAsync(CancellationToken.None);
        var operation = await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("requested", operation.Status);
        Assert.Null(operation.CompletedAt);
        Assert.False((await this.Service(context).GetAsync(id))!.CanRefund);
        providerChecked = true;
        this._clock.Advance(TimeSpan.FromMinutes(1));
        await this.Worker(context, handler).ProcessDueAsync(CancellationToken.None);
        operation = await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("succeeded", operation.Status);
        Assert.NotNull(operation.CompletedAt);
        Assert.True((await this.Service(context).GetAsync(id))!.CanRefund);
    }

    [Fact]
    public async Task NewConflictBetweenRetryAcceptanceAndExecutionPreventsAccessGrant()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(id))!;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "retry-access", "Повторить выдачу", null, state.StateToken), "admin");
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "attention", "access_transaction_failed", this._clock.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync();
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Equal("failed", (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == operationId)).Status);
    }

    [Fact]
    public async Task LateWorkerFailureCannotOverwriteCompletedOperation()
    {
        var id = await this.SeedAsync(fresh: false);
        await using var context = fixture.CreateContext();
        var state = (await this.Service(context).GetAsync(id))!;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "check", "Проверка", null, state.StateToken), "admin");
        var handler = new ConcurrentCompletionHandler(async () =>
        {
            await using var other = fixture.CreateContext();
            await other.SubscriptionPaymentAdminActions.Where(item => item.Id == operationId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, "succeeded")
                    .SetProperty(item => item.Code, "completed_by_other_worker")
                    .SetProperty(item => item.CompletedAt, this._clock.GetUtcNow().UtcDateTime));
        });
        await this.Worker(context, handler).ProcessDueAsync(CancellationToken.None);
        var result = await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync();
        Assert.Equal("succeeded", result.Status);
        Assert.Equal("completed_by_other_worker", result.Code);
    }

    [Fact]
    public async Task EmptyAdministrativeMigrationsCanRollBackAndApplyAgain()
    {
        await using var context = fixture.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync("20261025071834_AddPaymentsServicePurchases");
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.SubscriptionPaymentAdminActions.ToListAsync());
    }

    [Theory]
    [InlineData(129, 128)]
    [InlineData(500, 450)]
    public async Task RefundWireLimitsKeepTheFullAdministrativeAudit(int reasonLength, int actorLength)
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var reason = new string('Ж', reasonLength);
        var actor = new string('a', actorLength);
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        Assert.Equal(202, (await this.Service(context).ExecuteAsync(id, new(operationId, "refund", reason, 1, token), actor)).StatusCode);
        var stored = await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == operationId);
        var command = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync(item => item.Id == operationId);
        var payload = JsonSerializer.Deserialize<RequestRefund>(command.Payload)!;
        Assert.Equal(reason, stored.Reason);
        Assert.Equal(actor, stored.ActorUserId);
        Assert.Equal(128, payload.Reason.Length);
        Assert.Equal(reason[..128], payload.Reason);
        Assert.True(payload.RequestedBy.Length <= 128);
        Assert.Equal(actorLength <= 128 ? actor : "admin-sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor))), payload.RequestedBy);
    }

    [Theory]
    [InlineData(127, 127)]
    [InlineData(126, 128)]
    public async Task ProviderReasonTruncationPreservesSurrogatePairs(int prefixLength, int wireLength)
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var reason = new string('Ж', prefixLength) + char.ConvertFromUtf32(0x1F600) + "Причина";
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "refund", reason, 1, token), "admin");
        var command = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync(item => item.Id == operationId);
        var payload = JsonSerializer.Deserialize<RequestRefund>(command.Payload)!;
        Assert.Equal(wireLength, payload.Reason.Length);
        Assert.Equal(reason[..wireLength], payload.Reason);
        Assert.DoesNotContain('\uFFFD', payload.Reason);
        Assert.False(char.IsHighSurrogate(payload.Reason[^1]));
        Assert.Equal(reason, (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == operationId)).Reason);
    }

    [Theory]
    [InlineData("command_expired", false, true)]
    [InlineData("payments_disabled", false, true)]
    [InlineData("invalid_refund", true, true)]
    [InlineData("invalid_refund", false, false)]
    [InlineData("provider_outcome_unknown", false, false)]
    public async Task OnlyProvenNoMoneyCommandRejectionsAllowNewRefundAfterFreshCheck(string code, bool invalidReason, bool retryAllowed)
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        var reason = invalidReason ? new string('Ж', 129) : "Первый возврат";
        var issuedAt = code == "command_expired" ? this._clock.GetUtcNow().AddHours(-24)
            : code == "invalid_refund" && !invalidReason ? this._clock.GetUtcNow().AddMinutes(6) : this._clock.GetUtcNow();
        var notAfter = code == "command_expired" ? this._clock.GetUtcNow().AddHours(-1) : this._clock.GetUtcNow().AddHours(23);
        var oldPayload = JsonSerializer.Serialize(new RequestRefund(operationId, "opencrm", purchase.Id, 10000, reason, "admin",
            IssuedAt: issuedAt, NotAfter: notAfter, MerchantAccountId: this._options.MerchantAccountId));
        // Прежняя версия могла сохранить несовместимое тело; триггер неизменяемости не отключается для подготовки теста.
        context.SubscriptionPaymentAdminActions.Add(new SubscriptionPaymentAdminAction
        {
            Id = operationId, PurchaseId = purchase.Id, Action = "refund", ActorUserId = "admin", Reason = reason,
            AmountMinor = 10000, StateToken = token, RequestHash = new string('B', 64),
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        context.SubscriptionPaymentCommands.Add(new SubscriptionPaymentCommand
        {
            Id = operationId, PurchaseId = purchase.Id, Kind = "refund", Payload = oldPayload,
            PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldPayload))),
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        var processor = new SubscriptionPaymentEventProcessor(context, Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentEventProcessor>.Instance);
        await processor.ReceiveAsync(new PaymentCommandRejected(Guid.NewGuid(), "opencrm", purchase.Id, operationId, code, this._clock.GetUtcNow()));
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        var state = (await this.Service(context).GetAsync(id))!;
        Assert.False(state.CanRefund);
        Assert.Equal("failed", (await context.SubscriptionPaymentAdminActions.AsNoTracking().SingleAsync(item => item.Id == operationId)).Status);
        await this.FreshAsync(context);
        state = (await this.Service(context).GetAsync(id))!;
        Assert.Equal(retryAllowed, state.CanRefund);
        Assert.Equal(code, (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).LastErrorCode);
        if (retryAllowed)
        {
            var nextId = Guid.NewGuid();
            Assert.Equal(202, (await this.Service(context).ExecuteAsync(id, new(nextId, "refund", "Новая отдельная попытка", 100, state.StateToken), "admin")).StatusCode);
            Assert.Equal(2, await context.SubscriptionPaymentCommands.CountAsync(item => item.Kind == "refund"));
        }
    }

    [Fact]
    public async Task ProviderOutcomeUnknownHasAnExplicitNonterminalMessageAndKeepsTheReservation()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "refund", "Возврат", 100, token), "admin");
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "refund_operation_outcomeunknown", "provider_outcome_unknown",
            this._clock.GetUtcNow().UtcDateTime, operationId);
        await context.SaveChangesAsync();
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        var state = (await this.Service(context).GetAsync(id))!;
        var operation = state.Operations.Single(item => item.Id == operationId);
        Assert.Equal("needs_attention", operation.Status);
        Assert.Equal("refund_outcome_unknown", operation.Code);
        Assert.Contains("не создавайте новый возврат", operation.Message);
        Assert.Null(operation.CompletedAt);
        Assert.False(state.CanRefund);
        Assert.False(state.CanRetryAccess);
    }

    [Fact]
    public async Task OldNoMoneyRejectionCannotAuthorizeANewConflictWithTheSameCode()
    {
        var id = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var token = (await this.Service(context).GetAsync(id))!.StateToken;
        var operationId = Guid.NewGuid();
        await this.Service(context).ExecuteAsync(id, new(operationId, "refund", "Истекшая команда", 100, token), "admin");
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        this._clock.Advance(TimeSpan.FromHours(24));
        var processor = new SubscriptionPaymentEventProcessor(context, Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentEventProcessor>.Instance);
        await processor.ReceiveAsync(new PaymentCommandRejected(Guid.NewGuid(), "opencrm", purchase.Id, operationId,
            "command_expired", this._clock.GetUtcNow()));
        await this.Worker(context).ProcessDueAsync(CancellationToken.None);
        await this.FreshAsync(context);
        Assert.True((await this.Service(context).GetAsync(id))!.CanRefund);
        this._clock.Advance(TimeSpan.FromMinutes(1));
        SubscriptionPurchaseDiagnostics.Add(context, purchase, "attention", "command_expired", this._clock.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync();
        await this.FreshAsync(context);
        Assert.False((await this.Service(context).GetAsync(id))!.CanRefund);
    }

    private async Task<int> SeedAsync(bool fresh = true)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "admin-actions-" + Guid.NewGuid().ToString("N") + "@example.test" };
        var plan = new SubscriptionPlan { Name = "Тест действий", Price = 420, DurationDays = 30, VehicleLimit = 5,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal };
        context.Users.Add(user);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();
        var gateway = new MicroservicePaymentCreationGateway(context, Options.Create(this._options), Mock.Of<IPromotionPaymentCoordinator>(),
            Mock.Of<IOrganizationPermissionService>(), this._clock, NullLogger<MicroservicePaymentCreationGateway>.Instance);
        var intent = new PaymentCreationIntent(Guid.NewGuid(), user.Id, "yookassa_purchase", plan.Id, new PaymentBuyerDto { TypeCode = "individual" });
        await gateway.CreateAsync(new(intent, plan.Id, 420, "Проверка действий"));
        var processor = new SubscriptionPaymentEventProcessor(context, Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentEventProcessor>.Instance);
        await processor.ReceiveAsync(new PaymentStateChanged(Guid.NewGuid(), "opencrm", intent.OperationId, Guid.NewGuid(), 1,
            "Resolved", "Succeeded", 42000, 0, "RUB", "admin-payment-" + intent.OperationId, null, this._clock.GetUtcNow(), true));
        if (fresh)
        {
            await this.FreshAsync(context);
        }

        return await context.SubscriptionPurchases.Select(item => item.SubscriptionPaymentId).SingleAsync();
    }

    private async Task FreshAsync(OpenCRMContext context)
    {
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        context.SubscriptionPaymentAdminActions.Add(new SubscriptionPaymentAdminAction
        {
            Id = Guid.NewGuid(), PurchaseId = purchase.Id, Action = "check", ActorUserId = "admin", Reason = "Проверка зафиксирована тестом",
            RequestHash = new string('A', 64), VerifiedStateToken = await SubscriptionPaymentAdminService.StateTokenAsync(context, purchase, CancellationToken.None),
            Status = "succeeded", Code = "provider_check_confirmed", CreatedAt = this._clock.GetUtcNow().UtcDateTime,
            CompletedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
    }

    private SubscriptionPaymentAdminService Service(OpenCRMContext context) => new(context, Options.Create(this._options), this._clock,
        NullLogger<SubscriptionPaymentAdminService>.Instance);
    private SubscriptionPurchaseApplicator Applicator(OpenCRMContext context) => new(context, Mock.Of<IPromotionPaymentCoordinator>(),
        this._clock, NullLogger<SubscriptionPurchaseApplicator>.Instance);
    private SubscriptionPaymentAdminWorker Worker(OpenCRMContext context, HttpMessageHandler? handler = null)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(item => item.CreateClient("OpenCRM.PaymentsService")).Returns(() => new HttpClient(handler ?? new UnavailableHandler(), disposeHandler: false)
        { BaseAddress = new Uri(this._options.BaseUrl) });
        var processor = new SubscriptionPaymentEventProcessor(context, Options.Create(this._options), this._clock, NullLogger<SubscriptionPaymentEventProcessor>.Instance);
        var reconciler = new SubscriptionPaymentReconciler(context, clients.Object, processor, Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance);

        return new(context, clients.Object, reconciler, this.Applicator(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentAdminWorker>.Instance);
    }

    private sealed class SnapshotHandler(Func<object> snapshot, Guid purchaseId, TimeProvider clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(request.Method == HttpMethod.Post
                    ? new { PurchaseId = purchaseId, RequestedAt = clock.GetUtcNow() } : snapshot()))
            });
    }

    private sealed class ConcurrentCompletionHandler(Func<Task> complete) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await complete();

            throw new HttpRequestException("Поздний ответ потерян.");
        }
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("secret-provider-body-not-to-log");
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public override DateTimeOffset GetUtcNow() => this._now;
        public void Advance(TimeSpan amount) => this._now += amount;
    }
}
