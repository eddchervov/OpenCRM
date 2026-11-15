using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Testing;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentsServicePurchaseTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>, IAsyncLifetime
{
    private readonly TestClock _clock = new();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _providerId = "payments-test-" + Guid.NewGuid().ToString("N");
    private readonly PaymentsServiceOptions _options = new()
    {
        Enabled = true,
        MerchantAccountId = "123456789",
        BaseUrl = "https://payments.example.test/",
        ServiceKey = "test-service-key-never-log-0000000000000000",
        ReturnUrl = "https://app.example.test/payment/complete",
        AllowedConfirmationHosts = ["provider.example.test"]
    };

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PurchaseCommitsSnapshotCommandAndPaymentBeforeDeliveryAndResumesWithoutRepricing()
    {
        var (user, plan) = await this.SeedAsync();
        var intent = Intent(user.Id, plan.Id);
        await using (var context = fixture.CreateContext())
        {
            var result = await this.Gateway(context).CreateAsync(new(intent, plan.Id, 420m, "Покупка"));
            Assert.Equal(PaymentCreationStatus.InProgress, result.Status);
            Assert.Equal(intent.OperationId, result.PurchaseId);
        }

        await using var verification = fixture.CreateContext();
        var purchase = await verification.SubscriptionPurchases.Include(item => item.Payment).SingleAsync();
        var command = JsonSerializer.Deserialize<CreatePayment>((await verification.SubscriptionPaymentCommands.SingleAsync()).Payload)!;
        Assert.Equal(purchase.Id, command.CommandId);
        Assert.Equal(42000, command.AmountMinor);
        Assert.EndsWith("?purchaseId=" + purchase.Id.ToString("D"), command.ReturnUrl);
        Assert.Null(purchase.Payment.ExternalPaymentId);
        var changedPlan = await verification.SubscriptionPlans.SingleAsync(item => item.Id == plan.Id);
        changedPlan.Price = 999;
        changedPlan.DurationDays = 1;
        changedPlan.IsActive = false;
        await verification.SaveChangesAsync();

        await using var retry = fixture.CreateContext();
        Assert.Equal(PaymentCreationStatus.InProgress, (await this.Gateway(retry).ResumeAsync(intent))!.Status);
        Assert.Equal(420, purchase.Payment.Amount);
        Assert.Equal(30, SubscriptionPlanSnapshot.Restore(purchase.PlanSnapshot).DurationDays);
        Assert.Single(await retry.SubscriptionPurchases.ToListAsync());
        Assert.Equal(PaymentCreationStatus.Conflict, (await this.Gateway(retry).ResumeAsync(intent with { UserId = "another-payer" }))!.Status);
    }

    [Fact]
    public async Task DifferentRequestIdsCannotOpenTwoPurchasesForOneBeneficiary()
    {
        var (user, plan) = await this.SeedAsync();
        var intents = new[] { Intent(user.Id, plan.Id), Intent(user.Id, plan.Id) };
        var results = await Task.WhenAll(intents.Select(async intent =>
        {
            await using var context = fixture.CreateContext();
            return await this.Gateway(context).CreateAsync(new(intent, plan.Id, 420, "Покупка"));
        }));
        Assert.Single(results, result => result.Status == PaymentCreationStatus.InProgress);
        Assert.Single(results, result => result.Status == PaymentCreationStatus.Conflict);
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.SubscriptionPaymentCommands.ToListAsync());
        Assert.Single(await verification.SubscriptionPayments.ToListAsync());
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderEventsGrantSnapshotDurationAndFeaturesExactlyOnce()
    {
        var purchaseId = await this.CreateAsync();
        await using (var change = fixture.CreateContext())
        {
            var planId = await change.SubscriptionPurchases.Select(item => item.CreationOperation.SubscriptionPlanId).SingleAsync();
            var plan = await change.SubscriptionPlans.Include(item => item.Features).SingleAsync(item => item.Id == planId);
            plan.DurationDays = 1;
            plan.Price = 999;
            plan.IsActive = false;
            plan.Features[0].BoolValue = false;
            await change.SaveChangesAsync();
        }

        var paid = this.Event(purchaseId, 2, "Succeeded");
        await using (var context = fixture.CreateContext())
        {
            await this.Processor(context).ReceiveAsync(paid);
            await this.Processor(context).ReceiveAsync(paid);
            await this.Processor(context).ReceiveAsync(paid with { EventId = Guid.NewGuid() });
            await this.Processor(context).ReceiveAsync(this.Event(purchaseId, 1, "Pending"));
            await this.Applicator(context).ApplyAsync(purchaseId);
            await this.Applicator(context).ApplyAsync(purchaseId);
        }

        await using var verification = fixture.CreateContext();
        var purchase = await verification.SubscriptionPurchases.SingleAsync();
        var subscription = await verification.UserSubscriptions.SingleAsync();
        Assert.Equal(this._clock.GetUtcNow().UtcDateTime.AddDays(30), subscription.EndDate);
        Assert.Equal(purchase.Id, subscription.SubscriptionPurchaseId);
        Assert.Equal(2, purchase.ProviderVersion);
        Assert.NotNull(purchase.AppliedAt);
        Assert.Single(await verification.UserSubscriptionHistory.ToListAsync());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(verification);
        services.AddSingleton(Mock.Of<IFeatureDefaultsInitializer>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = "true"
        }).Build());
        services.AddOpenCRMBilling();
        using var provider = services.BuildServiceProvider();
        Assert.True(await provider.GetRequiredService<IFeatureAccessService>().HasFeatureAsync(purchase.UserId, "payment_snapshot_feature"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("environment")]
    [InlineData("schema")]
    [InlineData("refund")]
    public async Task UntrustedOrMismatchedFinancialSnapshotCannotGrantAccess(string mismatch)
    {
        var id = await this.CreateAsync();
        var message = this.Event(id, 1, "Succeeded");
        message = mismatch switch
        {
            "amount" => message with { AmountMinor = 43000 },
            "currency" => message with { Currency = "USD" },
            "environment" => message with { IsTest = false },
            "schema" => message with { SchemaVersion = 2 },
            _ => message with { RefundedAmountMinor = -1 }
        };
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(message);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Null(purchase.Payment.PaidAt);
        Assert.Equal(0, purchase.ProviderVersion);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
    }

    [Fact]
    public async Task EventIdWithDifferentPayloadIsAuditedWithoutChangingAcceptedMoney()
    {
        var id = await this.CreateAsync();
        var message = this.Event(id, 1, "Pending");
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(message);
        await this.Processor(context).ReceiveAsync(message with { PaymentStatus = "Succeeded" });
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Null(purchase.Payment.PaidAt);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = "true"
        }).Build());
        services.AddOpenCRMBilling();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var details = await scope.ServiceProvider.GetRequiredService<IAdminBillingQueryService>()
            .GetPaymentAsync(purchase.SubscriptionPaymentId);
        Assert.True(details!.IsProblem);
        Assert.Contains(details.ProviderEvents, item => item.Code == "event_payload_conflict" && item.PurchaseId == id);
    }

    [Fact]
    public async Task PaidFactSurvivesApplicationFailureAndRetryDoesNotDuplicateAccess()
    {
        var id = await this.CreateAsync();
        await using (var context = fixture.CreateContext())
        {
            await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
            await context.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_payment_grant() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test grant failure'; END $$; CREATE TRIGGER fail_payment_grant BEFORE INSERT ON \"UserSubscriptions\" FOR EACH ROW EXECUTE FUNCTION fail_payment_grant();");
            try
            {
                await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                var pending = await context.SubscriptionPurchases.Include(item => item.Payment).SingleAsync();
                Assert.NotNull(pending.Payment.PaidAt);
                Assert.Null(pending.AppliedAt);
                Assert.Equal("access_transaction_failed", pending.LastErrorCode);
                Assert.Empty(await context.UserSubscriptions.ToListAsync());
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_payment_grant ON \"UserSubscriptions\"; DROP FUNCTION fail_payment_grant();");
            }
        }

        this._clock.Advance(TimeSpan.FromMinutes(2));
        await using var retry = fixture.CreateContext();
        await this.Applicator(retry).ApplyPendingAsync(CancellationToken.None);
        await this.Applicator(retry).ApplyPendingAsync(CancellationToken.None);
        Assert.Single(await retry.UserSubscriptions.ToListAsync());
        Assert.NotNull((await retry.SubscriptionPurchases.SingleAsync()).AppliedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullRefundNeverGrantsNewAccessOrAutomaticallyRevokesExistingAccess(bool applied)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        if (applied)
        {
            await this.Applicator(context).ApplyAsync(id);
        }

        await this.Processor(context).ReceiveAsync(this.Event(id, 2, "Succeeded") with { RefundedAmountMinor = 42000 });
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal("refunded", purchase.Payment.PaymentStatus);
        Assert.Equal(applied ? 1 : 0, await context.UserSubscriptions.CountAsync());
        if (applied)
        {
            Assert.Equal("refund_requires_access_review", purchase.LastErrorCode);
            Assert.Equal(this._clock.GetUtcNow().UtcDateTime.AddDays(30), (await context.UserSubscriptions.SingleAsync()).EndDate);
        }

        Assert.Equal(applied ? PaymentCreationStatus.ManualReview : PaymentCreationStatus.Refunded,
            (await this.Gateway(context).GetAsync(purchase.UserId, id))!.Result.Status);
    }

    [Fact]
    public async Task LateCommandRejectionCannotCancelVerifiedPaidPurchaseAndQueryIsOwnerBound()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        await this.Processor(context).ReceiveAsync(new PaymentCommandRejected(Guid.NewGuid(), "opencrm", id, id, "late_rejection", this._clock.GetUtcNow()));
        await this.Applicator(context).ApplyAsync(id);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.NotNull(purchase.AppliedAt);
        Assert.Null(await this.Gateway(context).GetAsync("another-payer", id));
        Assert.Equal(PaymentCreationStatus.Completed, (await this.Gateway(context).GetAsync(purchase.UserId, id))!.Result.Status);
    }

    [Fact]
    public async Task LostBrokerAcknowledgementAndRestartReuseIdenticalSavedCommand()
    {
        var id = await this.CreateAsync();
        var sender = new RecordingTransport { FailFirst = true };
        await using (var context = fixture.CreateContext())
        {
            await this.Dispatcher(context, sender).DispatchAsync(CancellationToken.None);
        }

        this._clock.Advance(TimeSpan.FromMinutes(2));
        await using (var context = fixture.CreateContext())
        {
            await this.Dispatcher(context, sender).DispatchAsync(CancellationToken.None);
        }

        var sent = sender.Commands.ToArray();
        Assert.Equal(2, sent.Length);
        Assert.Equal(sent[0], sent[1]);
        Assert.Equal(id, sent[0].Id);
        await using var verification = fixture.CreateContext();
        Assert.NotNull((await verification.SubscriptionPaymentCommands.SingleAsync()).SentAt);
        Assert.Single(await verification.SubscriptionPayments.ToListAsync());
        Assert.Contains(await verification.SubscriptionPaymentEvents.ToListAsync(), item => item.Code == "broker_delivery_unknown");
    }

    [Fact]
    public async Task PostgreSqlRejectsMutationOfAcceptedFinancialSnapshotAndNonemptyRollback()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => context.SubscriptionPurchases.Where(item => item.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.AmountMinor, 1)));
        await Assert.ThrowsAnyAsync<Exception>(() => context.SubscriptionPaymentCommands
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Payload, "{}")));
        await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync("20261024165338_AddOrganizationRepeatVisits"));
        // Откат запрещён не с первого шага: часть объектов успевает сняться, прежде чем срабатывает защита данных.
        // Возвращаем схему к текущей модели, чтобы следующие проверки класса работали на актуальном состоянии.
        await context.Database.MigrateAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => context.SubscriptionPaymentEvents.ExecuteDeleteAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => context.SubscriptionPaymentEvents
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.Code, "changed")));
        Assert.Equal(42000, (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).AmountMinor);
    }

    [Fact]
    public async Task UnknownPurchaseEventIsDurableAndCanBeReplayedAfterLocalPurchaseRecovery()
    {
        var (user, plan) = await this.SeedAsync();
        var intent = Intent(user.Id, plan.Id);
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(intent.OperationId, 1, "Succeeded"));
        Assert.Null((await context.SubscriptionPaymentEvents.SingleAsync(item => item.MessageId != null)).ProcessedAt);
        await this.Gateway(context).CreateAsync(new(intent, plan.Id, 420, "Покупка"));
        this._clock.Advance(TimeSpan.FromMinutes(2));
        await this.Processor(context).ReplayPendingAsync(CancellationToken.None);
        await this.Applicator(context).ApplyAsync(intent.OperationId);
        Assert.Single(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task ServiceReadFailureIsDurableAndLogsExcludeHeadersAndExceptionSecrets()
    {
        var id = await this.CreateAsync();
        this._clock.Advance(TimeSpan.FromMinutes(2));
        var journal = new SafeTestLogger<SubscriptionPaymentReconciler>();
        using var client = new HttpClient(new FailingHandler(this._options.ServiceKey)) { BaseAddress = new Uri(this._options.BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Payments-Key", this._options.ServiceKey);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await using var context = fixture.CreateContext();
        var reconciler = new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context),
            Options.Create(this._options), this._clock, journal);
        await reconciler.ReconcileDueAsync(CancellationToken.None);
        Assert.Contains(journal.Messages, item => item.Contains("service_reconciliation", StringComparison.Ordinal));
        Assert.DoesNotContain(journal.Messages, item => item.Contains(this._options.ServiceKey, StringComparison.Ordinal));
        Assert.DoesNotContain(journal.Messages, item => item.Contains("Authorization", StringComparison.Ordinal));
        Assert.Contains(await context.SubscriptionPaymentEvents.ToListAsync(), item => item.Code == "service_read_unavailable");
        Assert.True((await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).NextReconcileAt > this._clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task ConflictingCommandRejectionKeepsReservationUntilFinancialOutcomeIsKnown()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(new PaymentCommandRejected(Guid.NewGuid(), "opencrm", id, id,
            "command_payload_conflict", this._clock.GetUtcNow()));
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.True(purchase.IsOpen);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal("command_payload_conflict", purchase.LastErrorCode);
        Assert.Null(purchase.Payment.PaidAt);
    }

    [Fact]
    public async Task PartialRefundBeforeActivationRequiresManualReviewAndNeverGrantsAccess()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded") with { RefundedAmountMinor = 100 });
        await this.Applicator(context).ApplyAsync(id);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        var purchase = await context.SubscriptionPurchases.Include(item => item.Payment).AsNoTracking().SingleAsync();
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal(PaymentCreationStatus.ManualReview, MicroservicePaymentCreationGateway.Map(purchase).Status);
        Assert.NotNull(purchase.Payment.PaidAt);
    }

    [Fact]
    public async Task ProductApplicationIdIsUsedForNewPaymentCommandsAndEvents()
    {
        this._options.ApplicationId = "sample-crm";
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var command = JsonSerializer.Deserialize<CreatePayment>((await context.SubscriptionPaymentCommands.SingleAsync()).Payload)!;

        Assert.Equal("sample-crm", command.ApplicationId);
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded") with { ApplicationId = "sample-crm" });

        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal("Succeeded", purchase.ProviderStatus);
        Assert.Equal(SubscriptionPurchaseStatuses.ActivationPending, purchase.ApplicationStatus);
    }

    [Fact]
    public async Task EventFromAnotherApplicationIsRejected()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded") with { ApplicationId = "sample-crm" });

        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal("financial_snapshot_mismatch", purchase.LastErrorCode);
        Assert.NotEqual("Succeeded", purchase.ProviderStatus);
        Assert.Null(purchase.Payment.PaidAt);
    }

    [Fact]
    public async Task FinancialMismatchAfterSuccessBlocksActivationUntilReview()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        await this.Processor(context).ReceiveAsync(this.Event(id, 2, "Succeeded") with { Currency = "USD" });
        await this.Applicator(context).ApplyAsync(id);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Equal("financial_snapshot_mismatch", (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).LastErrorCode);
    }

    [Fact]
    public async Task RejectedCommandRedeliveryWithDifferentTimestampDoesNotCreatePayloadConflict()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var message = new PaymentCommandRejected(Guid.NewGuid(), "opencrm", id, id, "payments_disabled", this._clock.GetUtcNow());
        await this.Processor(context).ReceiveAsync(message);
        await this.Processor(context).ReceiveAsync(message with { OccurredAt = message.OccurredAt.AddHours(2) });
        Assert.Single(await context.SubscriptionPaymentEvents.Where(item => item.MessageId != null).ToListAsync());
        Assert.DoesNotContain(await context.SubscriptionPaymentEvents.ToListAsync(), item => item.Code == "event_payload_conflict");
        await this.Processor(context).ReceiveAsync(message with { Reason = "different_reason" });
        Assert.Contains(await context.SubscriptionPaymentEvents.ToListAsync(), item => item.Code == "event_payload_conflict");
    }

    [Theory]
    [InlineData("https://checkout.provider.example.test/payment", true)]
    [InlineData("https://evilprovider.example.test/payment", false)]
    [InlineData("https://provider.example.test.evil.test/payment", false)]
    public async Task ConfirmationHostAllowsOfficialSubdomainAndRejectsLookalikes(string url, bool accepted)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Pending") with { ConfirmationUrl = url });
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal(accepted ? PaymentCreationStatus.PaymentRequired : PaymentCreationStatus.ManualReview,
            MicroservicePaymentCreationGateway.Map(purchase).Status);
    }

    [Theory]
    [InlineData("Rejected", "refund_canceled")]
    [InlineData("ManualReview", "provider_mismatch")]
    public async Task RefundOperationFailureIsValidatedJournaledAndDoesNotChangeAccess(string status, string code)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        await this.Applicator(context).ApplyAsync(id);
        var commandId = Guid.NewGuid();
        context.SubscriptionPaymentCommands.Add(new SubscriptionPaymentCommand
        {
            Id = commandId, PurchaseId = id, Kind = "refund", Payload = "{}", PayloadHash = new string('A', 64),
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        await this.Processor(context).ReceiveAsync(new PaymentOperationStateChanged(Guid.NewGuid(), "opencrm", id,
            this._orderId, Guid.NewGuid(), commandId, "Refund", status, code, this._clock.GetUtcNow()));
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.Equal(0, purchase.RefundedAmountMinor);
        Assert.NotNull(purchase.AppliedAt);
        Assert.Single(await context.UserSubscriptions.ToListAsync());
        Assert.Equal(PaymentCreationStatus.ManualReview, MicroservicePaymentCreationGateway.Map(purchase).Status);
        Assert.Contains(await context.SubscriptionPaymentEvents.ToListAsync(), item => item.CommandId == commandId && item.Code == code);
    }

    [Fact]
    public async Task PendingHttpSnapshotKeepsOneMinuteRecoveryInterval()
    {
        var id = await this.CreateAsync();
        var snapshot = new
        {
            Id = this._orderId, ApplicationId = "opencrm", PurchaseId = id, Version = 1,
            AmountMinor = 42000, RefundedAmountMinor = 0, Currency = "RUB", PaymentStatus = "Pending",
            ProviderPaymentId = this._providerId, ConfirmationUrl = "https://provider.example.test/payment", IsTest = true,
            Operations = new[] { new { Kind = "CreatePayment", Status = "Resolved" } }
        };
        using var client = new HttpClient(new SnapshotHandler(JsonSerializer.Serialize(snapshot))) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await using var context = fixture.CreateContext();
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(this._clock.GetUtcNow().UtcDateTime.AddMinutes(1), purchase.NextReconcileAt);
        Assert.Equal("Pending", purchase.ProviderStatus);
    }

    [Fact]
    public async Task EmptyMigrationCanRollBackAndReapplyAndMatchesCurrentModel()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261024165338_AddOrganizationRepeatVisits");
        await migrator.MigrateAsync();
        Assert.Empty(await context.SubscriptionPurchases.ToListAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task RefundOutcomeBeforePaymentSnapshotIsReplayedAndKeepsCreationSuccessful()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var commandId = Guid.NewGuid();
        context.SubscriptionPaymentCommands.Add(new SubscriptionPaymentCommand
        {
            Id = commandId, PurchaseId = id, Kind = "refund", Payload = "{}", PayloadHash = new string('A', 64),
            CreatedAt = this._clock.GetUtcNow().UtcDateTime, NextAttemptAt = this._clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        var outcome = new PaymentOperationStateChanged(Guid.NewGuid(), "opencrm", id, this._orderId,
            Guid.NewGuid(), commandId, "Refund", "Rejected", "refund_canceled", this._clock.GetUtcNow());
        await this.Processor(context).ReceiveAsync(outcome);
        Assert.Null((await context.SubscriptionPaymentEvents.SingleAsync(item => item.MessageId == outcome.EventId)).ProcessedAt);
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        this._clock.Advance(TimeSpan.FromMinutes(2));
        await this.Processor(context).ReplayPendingAsync(CancellationToken.None);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.CreationOperation).SingleAsync();
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal("refund_canceled", purchase.LastErrorCode);
        Assert.Equal(PaymentCreationOperationStatuses.Created, purchase.CreationOperation.Status);
        Assert.NotNull((await context.SubscriptionPaymentEvents.SingleAsync(item => item.MessageId == outcome.EventId)).ProcessedAt);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("ManualReview")]
    [InlineData("Rejected")]
    public async Task ProviderCreationAttentionBlocksAutomaticGrantDespiteSucceededMoneySnapshot(string creationStatus)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded") with { CreationStatus = creationStatus });
        await this.Applicator(context).ApplyAsync(id);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).SingleAsync();
        Assert.NotNull(purchase.Payment.PaidAt);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationPurchaseAndExtensionUseSavedTermsEvenAfterActorLosesMembership(bool extension)
    {
        var (user, plan) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var storedPlan = await context.SubscriptionPlans.SingleAsync(item => item.Id == plan.Id);
        storedPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business;
        var organization = new Organization
        {
            Name = "Финансовая проверка", OwnerUserId = user.Id, PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            Members = [new OrganizationMember { UserId = user.Id, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        var now = this._clock.GetUtcNow().UtcDateTime;
        var baseDate = extension ? now.AddDays(2) : now;
        if (extension)
        {
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Organization = organization, SubscriptionPlanId = plan.Id, SourceCode = "paid",
                StartDate = now.AddDays(-28), EndDate = baseDate
            });
        }

        await context.SaveChangesAsync();
        var id = Guid.NewGuid();
        var kind = extension ? "extension" : "purchase";
        var intent = new PaymentCreationIntent(id, user.Id, $"yookassa_workspace_{kind}:{organization.Id:D}", plan.Id,
            new PaymentBuyerDto { TypeCode = "individual" });
        Assert.Equal(PaymentCreationStatus.InProgress, (await this.Gateway(context, true).CreateAsync(new(intent, plan.Id, 420, "Покупка"))).Status);
        await context.OrganizationMembers.Where(item => item.OrganizationId == organization.Id).ExecuteDeleteAsync();
        var changedPlan = await context.SubscriptionPlans.SingleAsync(item => item.Id == plan.Id);
        changedPlan.DurationDays = 365;
        changedPlan.IsActive = false;
        await context.SaveChangesAsync();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        await this.Applicator(context).ApplyAsync(id);
        await this.Applicator(context).ApplyAsync(id);
        var subscription = Assert.Single(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Equal(baseDate.AddDays(30), subscription.EndDate);
        Assert.Equal(id, subscription.SubscriptionPurchaseId);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(baseDate, purchase.GrantedFrom);
        Assert.NotNull(purchase.AppliedAt);
        Assert.Single(await context.OrganizationAuditEvents.Where(item => item.TargetId == id.ToString("D")).ToListAsync());
    }

    [Fact]
    public async Task StaleDiscountSnapshotCannotBypassCurrentCatalogPriceCheck()
    {
        var (user, plan) = await this.SeedAsync();
        await using var context = fixture.CreateContext();
        var organization = new Organization
        {
            Name = "Проверка скидки", OwnerUserId = user.Id,
            Members = [new OrganizationMember { UserId = user.Id, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        var storedPlan = await context.SubscriptionPlans.SingleAsync(item => item.Id == plan.Id);
        storedPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business;
        storedPlan.Price = 999;
        await context.SaveChangesAsync();
        var intent = new PaymentCreationIntent(Guid.NewGuid(), user.Id, $"yookassa_workspace_purchase:{organization.Id:D}", plan.Id,
            new PaymentBuyerDto { TypeCode = "individual" });
        var result = await this.Gateway(context, true).CreateAsync(new(intent, plan.Id, 400, "Покупка", new PaymentPromotionDto
        {
            PromoCodeId = 1, PromoCode = "OLD", OrganizationId = organization.Id, OriginalAmount = 420, DiscountAmount = 20
        }));
        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        Assert.Empty(await context.SubscriptionPaymentCommands.ToListAsync());
        Assert.Empty(await context.SubscriptionPayments.ToListAsync());
    }

    [Fact]
    public async Task AcknowledgedMissingServicePurchaseIsQuarantinedWithoutAnotherCreate()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var command = await context.SubscriptionPaymentCommands.SingleAsync();
        command.Attempts = 1;
        command.SentAt = this._clock.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync();
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        var transport = new RecordingTransport();
        await this.Dispatcher(context, transport).DispatchAsync(CancellationToken.None);
        Assert.Empty(transport.Commands);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.True(purchase.IsOpen);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal("service_purchase_missing_after_dispatch", purchase.LastErrorCode);
        var persisted = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync();
        Assert.Equal(1, persisted.Attempts);
        Assert.NotNull(persisted.SentAt);
        Assert.Equal(DateTime.MaxValue.Ticks, persisted.NextAttemptAt.Ticks);
        Assert.Single(await context.SubscriptionPayments.ToListAsync());
    }

    [Fact]
    public async Task MissingUnacknowledgedPurchaseResendsOriginalCreatePayloadOnce()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var command = await context.SubscriptionPaymentCommands.SingleAsync();
        var payload = command.Payload;
        var now = this._clock.GetUtcNow().UtcDateTime;
        command.Attempts = 1;
        command.NextAttemptAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        command.LastErrorCode = "service_purchase_missing_after_dispatch";
        var purchase = await context.SubscriptionPurchases.SingleAsync(item => item.Id == id);
        purchase.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        purchase.LastErrorCode = "service_purchase_missing_after_dispatch";
        await context.SaveChangesAsync();

        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        var journal = new SafeTestLogger<SubscriptionPaymentReconciler>();
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options),
            this._clock, journal).ReconcileAsync(id);

        var resumed = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync();
        var restored = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(payload, resumed.Payload);
        Assert.Equal(1, resumed.Attempts);
        Assert.Null(resumed.SentAt);
        Assert.Null(resumed.LastErrorCode);
        Assert.Equal(now, resumed.NextAttemptAt);
        Assert.Equal(SubscriptionPurchaseStatuses.AwaitingPayment, restored.ApplicationStatus);
        Assert.Null(restored.LastErrorCode);
        Assert.Contains(await context.SubscriptionPaymentEvents.ToListAsync(), item => item.Code == "create_dispatch_resumed");

        var transport = new RecordingTransport();
        await this.Dispatcher(context, transport).DispatchAsync(CancellationToken.None);
        var sent = Assert.Single(transport.Commands);
        Assert.Equal(resumed.Id, sent.Id);
        Assert.Equal(payload, sent.Payload);
        var delivered = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync();
        Assert.NotNull(delivered.SentAt);
        Assert.Equal(now, delivered.SentAt.Value);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("merchant")]
    public async Task MissingUnacknowledgedPurchaseWithUnsafePayloadRemainsQuarantined(string invalidation)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var command = await context.SubscriptionPaymentCommands.SingleAsync();
        if (invalidation == "expired")
        {
            this._clock.Advance(TimeSpan.FromHours(24));
        }

        command.Attempts = 1;
        var purchase = await context.SubscriptionPurchases.SingleAsync(item => item.Id == id);
        purchase.NextReconcileAt = this._clock.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync();
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        var options = invalidation == "merchant"
            ? new PaymentsServiceOptions { Enabled = true, ExpectedIsTest = true, MerchantAccountId = "another-merchant", ReturnUrl = "https://app.example.test/payment/complete" }
            : this._options;
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(options),
            this._clock, NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        var transport = new RecordingTransport();
        await this.Dispatcher(context, transport).DispatchAsync(CancellationToken.None);

        var persisted = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync();
        var blocked = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Empty(transport.Commands);
        Assert.Equal(1, persisted.Attempts);
        Assert.Null(persisted.SentAt);
        Assert.Equal(DateTime.MaxValue.Ticks, persisted.NextAttemptAt.Ticks);
        Assert.Equal("service_purchase_missing_after_dispatch", persisted.LastErrorCode);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, blocked.ApplicationStatus);
        Assert.Equal("service_purchase_missing_after_dispatch", blocked.LastErrorCode);
    }

    [Fact]
    public async Task MissingPurchaseWithActiveOutboxLeaseLeavesLeaseAndBackoffUnchanged()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var command = await context.SubscriptionPaymentCommands.SingleAsync();
        var now = this._clock.GetUtcNow().UtcDateTime;
        var leaseToken = Guid.NewGuid();
        var leaseUntil = now.AddMinutes(1);
        var nextAttemptAt = now.AddSeconds(-10);
        command.Attempts = 1;
        command.LastErrorCode = "broker_delivery_unknown";
        command.LeaseToken = leaseToken;
        command.LeaseUntil = leaseUntil;
        command.NextAttemptAt = nextAttemptAt;
        await context.SaveChangesAsync();
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);

        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options),
            this._clock, NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        var transport = new RecordingTransport();
        await this.Dispatcher(context, transport).DispatchAsync(CancellationToken.None);

        var persisted = await context.SubscriptionPaymentCommands.AsNoTracking().SingleAsync();
        Assert.Empty(transport.Commands);
        Assert.Equal(leaseToken, persisted.LeaseToken);
        Assert.Equal(leaseUntil, persisted.LeaseUntil);
        Assert.Equal(nextAttemptAt, persisted.NextAttemptAt);
        Assert.Equal("broker_delivery_unknown", persisted.LastErrorCode);
    }

    [Fact]
    public async Task MissingServicePurchaseBeforeFirstDispatchDoesNotQuarantineNewOutbox()
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        var transport = new RecordingTransport();
        await this.Dispatcher(context, transport).DispatchAsync(CancellationToken.None);
        Assert.Single(transport.Commands);
        Assert.Equal(SubscriptionPurchaseStatuses.AwaitingPayment, (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).ApplicationStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledTransportWithPurchasesFailsStartupInsteadOfSilentlyLosingFinancialObservation(bool applied)
    {
        await this.CreateAsync();
        if (applied)
        {
            await using var appliedContext = fixture.CreateContext();
            var purchase = await appliedContext.SubscriptionPurchases.SingleAsync();
            purchase.ProviderStatus = "Succeeded";
            purchase.IsOpen = false;
            purchase.AppliedAt = this._clock.GetUtcNow().UtcDateTime;
            purchase.ApplicationStatus = SubscriptionPurchaseStatuses.Applied;
            await appliedContext.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext());
        using var provider = services.BuildServiceProvider();
        var guard = new PaymentsServiceRecoveryGuard(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PaymentsServiceOptions { Enabled = false }), NullLogger<PaymentsServiceRecoveryGuard>.Instance);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));
        Assert.Contains("существующих покупках МС", exception.Message);
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.SubscriptionPurchases.ToListAsync());
        Assert.Null((await verification.SubscriptionPayments.SingleAsync()).PaidAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompletePrivateHttpBodyCannotHangRecoveryOrExposeBodyErrors(bool disconnect)
    {
        var id = await this.CreateAsync();
        this._clock.Advance(TimeSpan.FromMinutes(2));
        var logger = new SafeTestLogger<SubscriptionPaymentReconciler>();
        using var client = new HttpClient(new IncompleteBodyHandler(disconnect))
        {
            BaseAddress = new Uri(this._options.BaseUrl), Timeout = TimeSpan.FromMilliseconds(100)
        };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await using var context = fixture.CreateContext();
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock, logger)
            .ReconcileDueAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(this._clock.GetUtcNow().UtcDateTime.AddMinutes(5), purchase.NextReconcileAt);
        Assert.Equal("service_read_unavailable", purchase.LastErrorCode);
        Assert.DoesNotContain(logger.Messages, value => value.Contains("secret-body-value", StringComparison.Ordinal));
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class IncompleteBodyHandler(bool disconnect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new IncompleteBodyStream(disconnect)) });
    }

    private sealed class IncompleteBodyStream(bool disconnect) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (disconnect)
            {
                throw new IOException("secret-body-value");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task VerifiedPaidPurchaseStillGrantsAccessWhileServiceReadIsUnavailable(HttpStatusCode status)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        using var client = new HttpClient(new StatusHandler(status)) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        Assert.Equal(SubscriptionPurchaseStatuses.ActivationPending, (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).ApplicationStatus);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Single(await context.UserSubscriptions.ToListAsync());
        Assert.NotNull((await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).AppliedAt);
        Assert.Contains(await context.SubscriptionPaymentEvents.ToListAsync(), entry => entry.Kind == "service_reconciliation");
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Succeeded")]
    public async Task MatchingSameVersionSnapshotRestoresTransientAttention(string status)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, status));
        var blocked = await context.SubscriptionPurchases.SingleAsync();
        blocked.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        blocked.LastErrorCode = "service_http_401";
        await context.SaveChangesAsync();
        await this.ReadSnapshotAsync(context, id, 1, status);
        var restored = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Null(restored.LastErrorCode);
        Assert.Equal(status == "Succeeded" ? SubscriptionPurchaseStatuses.ActivationPending : SubscriptionPurchaseStatuses.AwaitingPayment,
            restored.ApplicationStatus);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Equal(status == "Succeeded" ? 1 : 0, await context.UserSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceFailuresCannotEraseFinancialConflictOrRefundReview(bool refund)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        var initial = this.Event(id, 1, refund ? "Succeeded" : "Pending");
        await this.Processor(context).ReceiveAsync(initial);
        if (refund)
        {
            await this.Applicator(context).ApplyAsync(id);
            await this.Processor(context).ReceiveAsync(this.Event(id, 2, "Succeeded") with { RefundedAmountMinor = 100 });
        }
        else
        {
            await this.Processor(context).ReceiveAsync(initial with { PaymentStatus = "Succeeded" });
        }

        var code = (await context.SubscriptionPurchases.AsNoTracking().SingleAsync()).LastErrorCode;
        using (var client = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized)) { BaseAddress = new Uri(this._options.BaseUrl) })
        {
            var clients = new Mock<IHttpClientFactory>();
            clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
            await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
                NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
        }

        await this.ReadSnapshotAsync(context, id, refund ? 2 : 1, refund ? "Succeeded" : "Pending", refund ? 100 : 0);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(code, purchase.LastErrorCode);
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal(refund ? 1 : 0, await context.UserSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameVersionWithDifferentMoneyCannotClearAttention(bool changedProvider)
    {
        var id = await this.CreateAsync();
        await using var context = fixture.CreateContext();
        await this.Processor(context).ReceiveAsync(this.Event(id, 1, "Succeeded"));
        var blocked = await context.SubscriptionPurchases.SingleAsync();
        blocked.ApplicationStatus = SubscriptionPurchaseStatuses.NeedsAttention;
        blocked.LastErrorCode = "service_http_401";
        await context.SaveChangesAsync();
        await this.ReadSnapshotAsync(context, id, 1, "Succeeded", changedProvider ? 0 : 100,
            changedProvider ? "another-provider-payment" : null);
        var purchase = await context.SubscriptionPurchases.AsNoTracking().SingleAsync();
        Assert.Equal(SubscriptionPurchaseStatuses.NeedsAttention, purchase.ApplicationStatus);
        Assert.Equal("financial_version_conflict", purchase.LastErrorCode);
        Assert.Equal(0, purchase.RefundedAmountMinor);
        await this.Applicator(context).ApplyPendingAsync(CancellationToken.None);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    private async Task ReadSnapshotAsync(OpenCRMContext context, Guid id, long version, string status, long refunded = 0, string? providerId = null)
    {
        var snapshot = new
        {
            Id = this._orderId, ApplicationId = "opencrm", PurchaseId = id, Version = version,
            AmountMinor = 42000, RefundedAmountMinor = refunded, Currency = "RUB", PaymentStatus = status,
            ProviderPaymentId = providerId ?? this._providerId, ConfirmationUrl = status == "Pending" ? "https://provider.example.test/payment" : null,
            IsTest = true, Operations = new[] { new { Kind = "CreatePayment", Status = "Resolved" } }
        };
        using var client = new HttpClient(new SnapshotHandler(JsonSerializer.Serialize(snapshot))) { BaseAddress = new Uri(this._options.BaseUrl) };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(factory => factory.CreateClient("OpenCRM.PaymentsService")).Returns(client);
        await new SubscriptionPaymentReconciler(context, clients.Object, this.Processor(context), Options.Create(this._options), this._clock,
            NullLogger<SubscriptionPaymentReconciler>.Instance).ReconcileAsync(id);
    }

    private sealed class SnapshotHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private sealed class FailingHandler(string secret) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Authorization: " + secret);
    }

    private sealed class SafeTestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.Messages.Add(formatter(state, exception) + exception);
        }
    }

    private async Task<(ApplicationUser User, SubscriptionPlan Plan)> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = "payments-" + Guid.NewGuid().ToString("N") + "@example.test" };
        var definition = await context.FeatureDefinitions.SingleOrDefaultAsync(item => item.Code == "payment_snapshot_feature")
            ?? new FeatureDefinition
            {
                Code = "payment_snapshot_feature", Name = "Оплаченная возможность", ValueType = FeatureValueType.Boolean,
                ScopeCode = FeatureCatalogCodes.Scopes.Personal
            };
        var plan = new SubscriptionPlan
        {
            Name = "МС тариф " + Guid.NewGuid().ToString("N"), Price = 420, DurationDays = 30, VehicleLimit = 7,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal,
            Features = [new SubscriptionPlanFeature
            {
                BoolValue = true,
                FeatureDefinition = definition
            }]
        };
        context.Users.Add(user);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();
        return (user, plan);
    }

    private async Task<Guid> CreateAsync()
    {
        var (user, plan) = await this.SeedAsync();
        var intent = Intent(user.Id, plan.Id);
        await using var context = fixture.CreateContext();
        Assert.Equal(PaymentCreationStatus.InProgress, (await this.Gateway(context).CreateAsync(new(intent, plan.Id, 420, "Покупка"))).Status);
        return intent.OperationId;
    }

    private static PaymentCreationIntent Intent(string userId, int planId) => new(Guid.NewGuid(), userId, "yookassa_purchase", planId,
        new PaymentBuyerDto { TypeCode = "individual" });
    private MicroservicePaymentCreationGateway Gateway(OpenCRMContext context, bool allowOrganization = false)
    {
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(allowOrganization);

        return new(context, Options.Create(this._options), Mock.Of<IPromotionPaymentCoordinator>(), permissions.Object, this._clock,
            NullLogger<MicroservicePaymentCreationGateway>.Instance);
    }
    private SubscriptionPaymentEventProcessor Processor(OpenCRMContext context) => new(context, Options.Create(this._options), this._clock,
        NullLogger<SubscriptionPaymentEventProcessor>.Instance);
    private SubscriptionPurchaseApplicator Applicator(OpenCRMContext context) => new(context, Mock.Of<IPromotionPaymentCoordinator>(), this._clock,
        NullLogger<SubscriptionPurchaseApplicator>.Instance);
    private SubscriptionPaymentOutboxDispatcher Dispatcher(OpenCRMContext context, RecordingTransport transport) => new(context, transport,
        Options.Create(this._options), this._clock, NullLogger<SubscriptionPaymentOutboxDispatcher>.Instance);
    private PaymentStateChanged Event(Guid id, long version, string status) => new(Guid.NewGuid(), "opencrm", id, this._orderId, version,
        "Resolved", status, 42000, 0, "RUB", this._providerId, status == "Pending" ? "https://provider.example.test/payment" : null,
        this._clock.GetUtcNow(), true);

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public override DateTimeOffset GetUtcNow() => this._now;
        public void Advance(TimeSpan value) => this._now += value;
    }

    private sealed class RecordingTransport : ISubscriptionPaymentCommandTransport
    {
        public ConcurrentQueue<(Guid Id, string Payload)> Commands { get; } = new();
        public bool FailFirst { get; set; }
        public Task SendAsync(string kind, string payload, Guid commandId, Guid purchaseId, CancellationToken cancellationToken)
        {
            this.Commands.Enqueue((commandId, payload));
            if (this.FailFirst)
            {
                this.FailFirst = false;
                throw new IOException("Ответ брокера потерян после приёма команды.");
            }

            return Task.CompletedTask;
        }
    }
}
