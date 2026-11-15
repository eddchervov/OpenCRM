using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentCreationGatewayTests(NotificationFixture fixture)
    : IClassFixture<NotificationFixture>, IAsyncLifetime
{
    private readonly List<HttpClient> _httpClients = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var client in this._httpClients)
        {
            client.Dispose();
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAndRetryAfterPlanPriceChangeKeepsSavedProviderRequest()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id);
        var providerPaymentId = $"gateway-price-{Guid.NewGuid():N}";
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            return this.CreateProviderResponse(request, providerPaymentId);
        });

        PaymentCreationResult firstResult;
        await using (var context = fixture.CreateContext())
        {
            var gateway = this.CreateGateway(context, handler);
            firstResult = await gateway.CreateAsync(new PaymentCreationDraft(intent, plan.Id, 420, "Платная подписка"));
        }

        await using (var retryContext = fixture.CreateContext())
        {
            var result = await this.CreateGateway(retryContext, handler).CreateAsync(
                new PaymentCreationDraft(intent, plan.Id, 455, "Платная подписка"));
            Assert.Equal(PaymentCreationStatus.PaymentRequired, result.Status);
            Assert.Equal(firstResult.PaymentId, result.PaymentId);
            Assert.Equal(firstResult.PaymentUrl, result.PaymentUrl);
        }

        Assert.Equal(PaymentCreationStatus.PaymentRequired, firstResult.Status);
        Assert.Equal(1, requestCount);
        await using var verification = fixture.CreateContext();
        var operation = await verification.PaymentCreationOperations.SingleAsync(
            item => item.IdempotenceKey == intent.OperationId.ToString("N"));
        Assert.Equal(420, operation.Amount);
        Assert.Equal(providerPaymentId,
            (await verification.SubscriptionPayments.SingleAsync(payment => payment.Id == operation.SubscriptionPaymentId)).ExternalPaymentId);
    }

    [Fact]
    public async Task ResumeAfterTimeoutAndNewContextReusesPersistedBodyAndKey()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id);
        var key = intent.OperationId.ToString("N");
        var requestBodies = new List<string>();
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync());
            Assert.Equal(key, request.Headers.GetValues("Idempotence-Key").Single());
            throw new HttpRequestException("Неизвестный результат запроса.");
        });

        await using (var context = fixture.CreateContext())
        {
            var processor = this.CreateProcessor(context, firstHandler);
            var returnUrl = "https://app.example.test/payment/complete";
            var result = await processor.CreatePaymentAsync(
                user.Id, plan.Id, 420, "Платная подписка", intent.OperationType,
                intent.Buyer!, returnUrl, null, key, null);
            Assert.False(result.IsSuccess);
        }

        var providerPaymentId = $"gateway-resume-{Guid.NewGuid():N}";
        var retryHandler = new StubHttpMessageHandler(async request =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync());
            Assert.Equal(key, request.Headers.GetValues("Idempotence-Key").Single());
            return await this.CreateProviderResponse(request, providerPaymentId);
        });
        await using (var retryContext = fixture.CreateContext())
        {
            var result = await this.CreateGateway(retryContext, retryHandler).ResumeAsync(intent);
            Assert.NotNull(result);
            Assert.Equal(PaymentCreationStatus.PaymentRequired, result.Status);
            Assert.Equal(providerPaymentId, result.PaymentId);
        }

        Assert.Equal(2, requestBodies.Count);
        Assert.Equal(requestBodies[0], requestBodies[1]);
    }

    [Fact]
    public async Task ConcurrentCreateWithSameIntentSendsOnlyOneProviderRequest()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            Interlocked.Increment(ref requestCount);
            requestStarted.SetResult();
            await releaseProvider.Task;
            return await this.CreateProviderResponse(request, $"gateway-concurrent-{Guid.NewGuid():N}");
        });
        await using var firstContext = fixture.CreateContext();
        var firstTask = this.CreateGateway(firstContext, firstHandler)
            .CreateAsync(new PaymentCreationDraft(intent, plan.Id, 425, "Платная подписка"));
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var secondContext = fixture.CreateContext();
        var secondHandler = new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var secondResult = await this.CreateGateway(secondContext, secondHandler)
            .CreateAsync(new PaymentCreationDraft(intent, plan.Id, 425, "Платная подписка"));
        releaseProvider.SetResult();
        var firstResult = await firstTask;

        Assert.Equal(PaymentCreationStatus.InProgress, secondResult.Status);
        Assert.Equal(PaymentCreationStatus.PaymentRequired, firstResult.Status);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task ResumeRejectsDifferentIdentityWithoutReturningPaymentDetails()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var (anotherUser, _) = await this.CreateOwnerAndPlanAsync();
        var organizationId = Guid.NewGuid();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id) with
        {
            Buyer = new PaymentBuyerDto { TypeCode = "business", Name = "ООО Проверка", Inn = "7707083893" }
        };
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            return this.CreateProviderResponse(request, $"gateway-private-{Guid.NewGuid():N}");
        });
        await using (var context = fixture.CreateContext())
        {
            var created = await this.CreateGateway(context, handler)
                .CreateAsync(new PaymentCreationDraft(intent, plan.Id, 450, "Платная подписка"));
            Assert.Equal(PaymentCreationStatus.PaymentRequired, created.Status);
        }

        var mismatchedIntents = new[]
        {
            intent with { UserId = anotherUser.Id },
            intent with { OperationType = $"yookassa_workspace_purchase:{organizationId}" },
            intent with { OperationType = $"yookassa_workspace_extension:{organizationId}" },
            intent with { SubscriptionPlanId = plan.Id + 1 },
            intent with { Buyer = new PaymentBuyerDto { TypeCode = "individual" } },
            intent with { Buyer = new PaymentBuyerDto { TypeCode = "business", Name = "ООО Другое", Inn = "7707083893" } },
            intent with { Buyer = new PaymentBuyerDto { TypeCode = "business", Name = "ООО Проверка", Inn = "7707083886" } },
            intent with { ReturnUrl = "https://other.example.test/return" }
        };

        foreach (var mismatchedIntent in mismatchedIntents)
        {
            await using var context = fixture.CreateContext();
            var result = await this.CreateGateway(context, handler).ResumeAsync(mismatchedIntent);
            Assert.NotNull(result);
            Assert.Equal(PaymentCreationStatus.Conflict, result.Status);
            Assert.Null(result.PaymentId);
            Assert.Null(result.PaymentUrl);
        }

        Assert.Equal(1, requestCount);
    }

    [Theory]
    [InlineData("processing", true, true, PaymentCreationStatus.InProgress)]
    [InlineData("paid_waiting_confirmation", true, true, PaymentCreationStatus.InProgress)]
    [InlineData("waiting_for_capture", false, false, PaymentCreationStatus.InProgress)]
    [InlineData("pending", false, true, PaymentCreationStatus.PaymentRequired)]
    [InlineData("pending", true, true, PaymentCreationStatus.InProgress)]
    [InlineData("pending", false, false, PaymentCreationStatus.InProgress)]
    [InlineData("refunded", true, true, PaymentCreationStatus.Refunded)]
    [InlineData("completed", false, true, PaymentCreationStatus.Completed)]
    [InlineData("canceled", false, true, PaymentCreationStatus.Canceled)]
    [InlineData("failed", false, true, PaymentCreationStatus.Rejected)]
    [InlineData("failed", true, true, PaymentCreationStatus.ManualReview)]
    public async Task ResumeMapsPersistedPaymentStatus(
        string paymentStatus,
        bool hasPaidAt,
        bool hasPaymentLink,
        PaymentCreationStatus expectedStatus)
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var operationId = Guid.NewGuid();
        var externalPaymentId = $"gateway-status-{Guid.NewGuid():N}";
        var operation = new PaymentCreationOperation
        {
            IdempotenceKey = operationId.ToString("N"),
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            OperationType = "yookassa_purchase",
            Amount = 500,
            Description = "Платная подписка",
            BuyerTypeCode = "individual",
            RequestTimestamp = DateTime.UtcNow,
            RequestBody = "{}",
            CreatedAt = DateTime.UtcNow,
            Status = PaymentCreationOperationStatuses.Created,
            ExternalPaymentId = externalPaymentId,
            PaymentLink = hasPaymentLink ? "https://checkout.example.test/payment" : null,
            ReturnUrl = this.Config.ReturnUrl + "/payment/complete"
        };
        operation.SubscriptionPayment = new SubscriptionPayment
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            Amount = 500,
            CreatedAt = DateTime.UtcNow,
            PaymentMethod = operation.OperationType,
            PaymentStatus = paymentStatus,
            PaidAt = hasPaidAt ? DateTime.UtcNow : null,
            ExternalPaymentId = externalPaymentId,
            PaymentLink = operation.PaymentLink,
            BuyerTypeCode = operation.BuyerTypeCode
        };
        await using var setup = fixture.CreateContext();
        setup.PaymentCreationOperations.Add(operation);
        await setup.SaveChangesAsync();

        await using var context = fixture.CreateContext();
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));
        var result = await this.CreateGateway(context, handler)
            .ResumeAsync(this.CreateIntent(operationId, user.Id, plan.Id));

        Assert.NotNull(result);
        Assert.Equal(expectedStatus, result.Status);
        if (expectedStatus == PaymentCreationStatus.PaymentRequired)
        {
            Assert.Equal(externalPaymentId, result.PaymentId);
            Assert.Equal(operation.PaymentLink, result.PaymentUrl);
        }
    }

    [Fact]
    public async Task MissingReturnUrlKeepsStoredRedirectAfterServerConfigurationChanges()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id);
        var providerId = $"gateway-url-{Guid.NewGuid():N}";
        var calls = 0;
        var handler = new StubHttpMessageHandler(async request =>
        {
            calls++;
            return await this.CreateProviderResponse(request, providerId);
        });
        await using var first = fixture.CreateContext();
        var created = await this.CreateGateway(first, handler).CreateAsync(new PaymentCreationDraft(intent, plan.Id, 500, "Оплата"));
        Assert.Equal(PaymentCreationStatus.PaymentRequired, created.Status);
        await using var restarted = fixture.CreateContext();
        var configuration = this.Config;
        configuration.ReturnUrl = "https://new-app.example.test";
        var gateway = new YooKassaPaymentCreationGateway(restarted, this.CreateProcessor(restarted, handler), Options.Create(configuration));
        var replay = await gateway.ResumeAsync(intent);
        Assert.Equal(PaymentCreationStatus.PaymentRequired, replay!.Status);
        Assert.Equal(created.PaymentId, replay.PaymentId);
        var explicitChange = await gateway.ResumeAsync(intent with { ReturnUrl = configuration.ReturnUrl + "/payment/complete" });
        Assert.Equal(PaymentCreationStatus.Conflict, explicitChange!.Status);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(PaymentCreationOperationStatuses.Rejected, "failed", PaymentCreationStatus.Rejected)]
    [InlineData(PaymentCreationOperationStatuses.ManualReview, "failed", PaymentCreationStatus.ManualReview)]
    [InlineData(PaymentCreationOperationStatuses.Created, null, PaymentCreationStatus.ManualReview)]
    public async Task ResumeMapsTerminalOperationsAndMissingPayment(
        string operationStatus,
        string? paymentStatus,
        PaymentCreationStatus expectedStatus)
    {
        var (user, plan, operationId) = await this.CreateStoredOperationAsync(operationStatus, paymentStatus);
        await using var context = fixture.CreateContext();
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));

        var result = await this.CreateGateway(context, handler)
            .ResumeAsync(this.CreateIntent(operationId, user.Id, plan.Id));

        Assert.NotNull(result);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public async Task ResumeMapsUnrecognizedPaymentStatusToManualReview()
    {
        var (user, plan, operationId) = await this.CreateStoredOperationAsync(
            PaymentCreationOperationStatuses.Created,
            "authorized");
        await using var context = fixture.CreateContext();
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));

        var result = await this.CreateGateway(context, handler)
            .ResumeAsync(this.CreateIntent(operationId, user.Id, plan.Id));

        Assert.NotNull(result);
        Assert.Equal(PaymentCreationStatus.ManualReview, result.Status);
    }

    [Fact]
    public async Task ResumeFailsClosedForLegacyUnknownOperationStatus()
    {
        var (user, plan, operationId) = await this.CreateStoredOperationAsync(
            PaymentCreationOperationStatuses.Created,
            "pending");
        var idempotenceKey = operationId.ToString("N");
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"PaymentCreationOperations\" DROP CONSTRAINT \"CK_PaymentCreationOperations_Status\"");

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"PaymentCreationOperations\" SET \"Status\" = {"legacy_unknown"} WHERE \"IdempotenceKey\" = {idempotenceKey}");

            await using var queryContext = fixture.CreateContext();
            var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));
            var result = await this.CreateGateway(queryContext, handler)
                .ResumeAsync(this.CreateIntent(operationId, user.Id, plan.Id));

            Assert.NotNull(result);
            Assert.Equal(PaymentCreationStatus.ManualReview, result.Status);
            Assert.Null(result.PaymentUrl);
        }
        finally
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"PaymentCreationOperations\" SET \"Status\" = {PaymentCreationOperationStatuses.Created} WHERE \"IdempotenceKey\" = {idempotenceKey}");
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"PaymentCreationOperations\" ADD CONSTRAINT \"CK_PaymentCreationOperations_Status\" CHECK (\"Status\" IN ('prepared', 'sending', 'created', 'rejected', 'manual_review'))");
        }
    }

    [Fact]
    public async Task DirtyContextAndTransactionsAreRejectedBeforeDatabaseWork()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var intent = this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id);
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));
        await using (var dirtyContext = fixture.CreateContext())
        {
            dirtyContext.Users.Add(new ApplicationUser { Id = $"dirty-{Guid.NewGuid():N}" });
            var gateway = this.CreateGateway(dirtyContext, handler);
            Assert.Equal(
                PaymentCreationStatus.Invalid,
                (await gateway.CreateAsync(new PaymentCreationDraft(intent, plan.Id, 500, "Оплата"))).Status);
            Assert.Equal(PaymentCreationStatus.Invalid, (await gateway.ResumeAsync(intent))!.Status);
        }

        await using (var transactionalContext = fixture.CreateContext())
        await using (await transactionalContext.Database.BeginTransactionAsync())
        {
            var gateway = this.CreateGateway(transactionalContext, handler);
            Assert.Equal(
                PaymentCreationStatus.Invalid,
                (await gateway.CreateAsync(new PaymentCreationDraft(intent, plan.Id, 500, "Оплата"))).Status);
            Assert.Equal(PaymentCreationStatus.Invalid, (await gateway.ResumeAsync(intent))!.Status);
        }

        using var scope = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            new System.Transactions.TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.Serializable },
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        await using var ambientContext = fixture.CreateContext();
        var ambientGateway = this.CreateGateway(ambientContext, handler);
        Assert.Equal(
            PaymentCreationStatus.Invalid,
            (await ambientGateway.CreateAsync(new PaymentCreationDraft(intent, plan.Id, 500, "Оплата"))).Status);
        Assert.Equal(PaymentCreationStatus.Invalid, (await ambientGateway.ResumeAsync(intent))!.Status);
    }

    [Fact]
    public async Task ResumeReturnsNullWhenOperationDoesNotExist()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        await using var context = fixture.CreateContext();
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));

        var result = await this.CreateGateway(context, handler)
            .ResumeAsync(this.CreateIntent(Guid.NewGuid(), user.Id, plan.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExtensionIntentWithoutPlanMatchesStoredOperationAndExplicitReturnUrlCanMatch()
    {
        var (user, plan, operationId) = await this.CreateStoredOperationAsync(
            PaymentCreationOperationStatuses.Rejected,
            "failed",
            operationType: "yookassa_extension");
        await using var context = fixture.CreateContext();
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP не должен вызываться."));
        var intent = new PaymentCreationIntent(
            operationId,
            user.Id,
            "yookassa_extension",
            SubscriptionPlanId: null,
            new PaymentBuyerDto { TypeCode = "individual" },
            this.Config.ReturnUrl + "/payment/complete");

        var result = await this.CreateGateway(context, handler).ResumeAsync(intent);

        Assert.NotNull(result);
        Assert.Equal(PaymentCreationStatus.Rejected, result.Status);
    }

    private async Task<(ApplicationUser User, SubscriptionPlan Plan)> CreateOwnerAndPlanAsync()
    {
        var user = new ApplicationUser { Id = $"payment-gateway-{Guid.NewGuid():N}" };
        var plan = new SubscriptionPlan
        {
            Name = "Проверка gateway",
            Price = 500,
            DurationDays = 30,
            IsActive = true
        };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        return (user, plan);
    }

    private async Task<(ApplicationUser User, SubscriptionPlan Plan, Guid OperationId)> CreateStoredOperationAsync(
        string operationStatus,
        string? paymentStatus,
        string operationType = "yookassa_purchase")
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var operationId = Guid.NewGuid();
        var paymentId = $"gateway-stored-{Guid.NewGuid():N}";
        var operation = new PaymentCreationOperation
        {
            IdempotenceKey = operationId.ToString("N"),
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            OperationType = operationType,
            Amount = 500,
            Description = "Платная подписка",
            BuyerTypeCode = "individual",
            RequestTimestamp = DateTime.UtcNow,
            RequestBody = "{}",
            CreatedAt = DateTime.UtcNow,
            Status = operationStatus,
            ExternalPaymentId = paymentStatus == null ? null : paymentId,
            PaymentLink = paymentStatus == "pending" ? "https://checkout.example.test/payment" : null,
            ReturnUrl = this.Config.ReturnUrl + "/payment/complete"
        };

        if (paymentStatus != null)
        {
            operation.SubscriptionPayment = new SubscriptionPayment
            {
                UserId = user.Id,
                SubscriptionPlanId = plan.Id,
                Amount = 500,
                CreatedAt = DateTime.UtcNow,
                PaymentMethod = operationType,
                PaymentStatus = paymentStatus,
                PaidAt = paymentStatus == "failed" ? DateTime.UtcNow : null,
                ExternalPaymentId = paymentId,
                PaymentLink = operation.PaymentLink,
                BuyerTypeCode = "individual"
            };
        }

        await using var context = fixture.CreateContext();
        context.PaymentCreationOperations.Add(operation);
        await context.SaveChangesAsync();

        return (user, plan, operationId);
    }

    private PaymentCreationIntent CreateIntent(Guid operationId, string userId, int? planId) =>
        new(operationId, userId, "yookassa_purchase", planId, new PaymentBuyerDto { TypeCode = "individual" });

    private YooKassaPaymentCreationGateway CreateGateway(OpenCRMContext context, HttpMessageHandler handler) =>
        new(context, this.CreateProcessor(context, handler), Options.Create(this.Config));

    private PaymentCreationProcessor CreateProcessor(OpenCRMContext context, HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://payments.example.test/v3/") };
        this._httpClients.Add(client);
        return new PaymentCreationProcessor(client, context, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, this.Config);
    }

    private YooKassaConfig Config => new()
    {
        ShopId = "shop",
        SecretKey = "secret",
        ReturnUrl = "https://app.example.test"
    };

    private async Task<HttpResponseMessage> CreateProviderResponse(HttpRequestMessage request, string paymentId)
    {
        using var requestJson = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var requestRoot = requestJson.RootElement;
        var amount = requestRoot.GetProperty("amount");
        var metadata = requestRoot.GetProperty("metadata");
        var response = new
        {
            id = paymentId,
            status = "pending",
            paid = false,
            created_at = DateTime.UtcNow.ToString("O"),
            amount = new { value = amount.GetProperty("value").GetString(), currency = amount.GetProperty("currency").GetString() },
            confirmation = new { type = "redirect", confirmation_url = "https://checkout.example.test/payment" },
            metadata = new
            {
                userId = metadata.GetProperty("userId").GetString(),
                planId = metadata.GetProperty("planId").GetString(),
                timestamp = metadata.GetProperty("timestamp").GetString()
            }
        };

        return new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
