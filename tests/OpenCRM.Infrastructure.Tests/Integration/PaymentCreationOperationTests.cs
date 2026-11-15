using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentCreationOperationTests(NotificationFixture fixture)
    : IClassFixture<NotificationFixture>, IAsyncLifetime
{
    private readonly List<HttpClient> _httpClients = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var httpClient in this._httpClients)
        {
            httpClient.Dispose();
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SavesOperationBeforeSendingAndRecoversAlreadyPaidProviderPayment()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        const string providerPaymentId = "provider-early-paid";
        var paymentProvider = new Mock<IYooKassaService>(MockBehavior.Strict);
        paymentProvider.Setup(service => service.GetLocalPaymentInfoAsync(providerPaymentId))
            .Returns(async () =>
            {
                await using var verification = fixture.CreateContext();
                return await verification.SubscriptionPayments.AsNoTracking()
                    .Where(payment => payment.ExternalPaymentId == providerPaymentId)
                    .Select(payment => new LocalPaymentInfoDto
                    {
                        UserId = payment.UserId,
                        PlanId = payment.SubscriptionPlanId,
                        Amount = payment.Amount,
                        PaymentStatus = payment.PaymentStatus,
                        PaymentMethod = payment.PaymentMethod
                    }).SingleOrDefaultAsync();
            });
        paymentProvider.Setup(service => service.GetPaymentStatusAsync(providerPaymentId)).ReturnsAsync(new PaymentResponse
        {
            IsSuccess = true,
            PaymentId = providerPaymentId,
            Status = "succeeded",
            Paid = true,
            Currency = "RUB",
            Amount = 250
        });
        paymentProvider.Setup(service => service.MarkPaymentPaidAsync(providerPaymentId)).ReturnsAsync(true);
        var personalActivation = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        personalActivation.Setup(service => service.ActivateNewSubscriptionAsync(user.Id, plan.Id, providerPaymentId))
            .Returns(async () =>
            {
                await using var activationContext = fixture.CreateContext();
                await activationContext.SubscriptionPayments
                    .Where(payment => payment.ExternalPaymentId == providerPaymentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(payment => payment.PaymentStatus, "completed"));
                return SubscriptionResult.CreateSuccess();
            });
        var organizations = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        await using var confirmationContext = fixture.CreateContext();
        var confirmation = new PaymentConfirmationService(organizations.Object, personalActivation.Object, paymentProvider.Object, confirmationContext);
        var webhook = new YooKassaWebhookService(confirmation, paymentProvider.Object, NullLogger<YooKassaWebhookService>.Instance);
        var notification = new YooKassaNotification { Event = "payment.succeeded", Object = new() { Id = providerPaymentId } };
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(idempotenceKey, request.Headers.GetValues("Idempotence-Key").Single());
            await using var verification = fixture.CreateContext();
            var operation = await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
            Assert.Equal(PaymentCreationOperationStatuses.Sending, operation.Status);
            Assert.NotNull(operation.FirstAttemptAt);
            Assert.False(await verification.SubscriptionPayments.AnyAsync(item => item.ExternalPaymentId == providerPaymentId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => webhook.HandleAsync(notification));

            return await this.CreateProviderResponse(request, providerPaymentId, "succeeded", true, includeConfirmation: false);
        });

        await using var context = fixture.CreateContext();
        var result = await this.CreateService(context, handler).CreatePaymentAsync(
            user.Id, plan.Id, 250, "Тариф", "yookassa_purchase", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(providerPaymentId, result.PaymentId);
        await webhook.HandleAsync(notification);
        await webhook.HandleAsync(notification);
        personalActivation.Verify(service => service.ActivateNewSubscriptionAsync(user.Id, plan.Id, providerPaymentId), Times.Once);
        organizations.VerifyNoOtherCalls();
        await using var savedContext = fixture.CreateContext();
        var savedOperation = await savedContext.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
        var savedPayment = await savedContext.SubscriptionPayments.SingleAsync(item => item.ExternalPaymentId == providerPaymentId);
        Assert.Equal(PaymentCreationOperationStatuses.Created, savedOperation.Status);
        Assert.Equal(savedOperation.RequestBody, await handler.RequestBodyAsync);
        Assert.Equal(250, savedPayment.Amount);
        Assert.Equal(user.Id, savedPayment.UserId);
        Assert.Equal(plan.Id, savedPayment.SubscriptionPlanId);
        Assert.Equal("completed", savedPayment.PaymentStatus);
        Assert.Null(savedPayment.PaymentLink);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterUnknownProviderResultReusesExactRequestBodyAndKey(bool providerRequestTimedOut)
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var externalPaymentId = $"provider-retry-{Guid.NewGuid():N}";
        var requestBodies = new List<string>();
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync());
            Assert.Equal(idempotenceKey, request.Headers.GetValues("Idempotence-Key").Single());
            if (providerRequestTimedOut)
            {
                throw new HttpRequestException("Неизвестный результат запроса к платежному сервису.");
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("[]")
            };
        });

        await using (var firstContext = fixture.CreateContext())
        {
            var firstResult = await this.CreateService(firstContext, firstHandler).CreatePaymentAsync(
                user.Id, plan.Id, 375, "Тариф", "personal_subscription", this.IndividualBuyer,
                idempotenceKey: idempotenceKey);
            Assert.False(firstResult.IsSuccess);
        }

        var secondHandler = new StubHttpMessageHandler(async request =>
        {
            requestBodies.Add(await request.Content!.ReadAsStringAsync());
            Assert.Equal(idempotenceKey, request.Headers.GetValues("Idempotence-Key").Single());
            return await this.CreateProviderResponse(request, externalPaymentId, "pending", false);
        });
        await using (var retryContext = fixture.CreateContext())
        {
            var retryResult = await this.CreateService(retryContext, secondHandler).CreatePaymentAsync(
                user.Id, plan.Id, 375, "Тариф", "personal_subscription", this.IndividualBuyer,
                idempotenceKey: idempotenceKey);
            await using var diagnostic = fixture.CreateContext();
            var operation = await diagnostic.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
            Assert.True(retryResult.IsSuccess, $"{retryResult.ErrorMessage}; {operation.Status}; {operation.LastErrorCode}");
        }

        Assert.Equal(2, requestBodies.Count);
        Assert.Equal(requestBodies[0], requestBodies[1]);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.PaymentCreationOperations.CountAsync(item => item.IdempotenceKey == idempotenceKey));
        Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(item => item.ExternalPaymentId == externalPaymentId));
    }

    [Fact]
    public async Task ReusingKeyWithChangedAmountDoesNotSendAnotherProviderRequest()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var requestCount = 0;
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            requestCount++;
            return await this.CreateProviderResponse(request, "provider-key-conflict", "pending", false);
        });

        await using (var firstContext = fixture.CreateContext())
        {
            var result = await this.CreateService(firstContext, firstHandler).CreatePaymentAsync(
                user.Id, plan.Id, 400, "Тариф", "personal_subscription", this.IndividualBuyer,
                idempotenceKey: idempotenceKey);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        await using var secondContext = fixture.CreateContext();
        var changedRequestResult = await this.CreateService(secondContext, firstHandler).CreatePaymentAsync(
            user.Id, plan.Id, 401, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);

        Assert.False(changedRequestResult.IsSuccess);
        Assert.Contains("другими параметрами", changedRequestResult.ErrorMessage);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task ReusingKeyWithAmountBeyondCurrencyPrecisionUsesNormalizedSnapshot()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var requestCount = 0;
        var promoCodeId = await this.CreatePromoCodeAsync();
        var handler = new StubHttpMessageHandler(request =>
        {
            requestCount++;
            return this.CreateProviderResponse(request, "provider-rounded-amount", "pending", false);
        });
        var promotion = new PaymentPromotionDto
        {
            PromoCodeId = promoCodeId,
            PromoCode = "Тест",
            OriginalAmount = 500.126m,
            DiscountAmount = 100.003m
        };

        await using (var context = fixture.CreateContext())
        {
            var result = await this.CreateService(context, handler).CreatePaymentAsync(
                user.Id, plan.Id, 400.123m, "Тариф", "personal_subscription", this.IndividualBuyer,
                promotion: promotion, idempotenceKey: idempotenceKey);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        await using (var retryContext = fixture.CreateContext())
        {
            var retryResult = await this.CreateService(retryContext, handler).CreatePaymentAsync(
                user.Id, plan.Id, 400.123m, "Тариф", "personal_subscription", this.IndividualBuyer,
                promotion: promotion, idempotenceKey: idempotenceKey);
            var operation = await retryContext.PaymentCreationOperations.AsNoTracking()
                .SingleAsync(item => item.IdempotenceKey == idempotenceKey);
            Assert.True(retryResult.IsSuccess, $"{retryResult.ErrorMessage}; {operation.Status}; {operation.LastErrorCode}");
        }

        Assert.Equal(1, requestCount);
        await using var verification = fixture.CreateContext();
        var savedOperation = await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
        var savedPayment = await verification.SubscriptionPayments.SingleAsync(item => item.ExternalPaymentId == "provider-rounded-amount");
        Assert.Equal(400.12m, savedOperation.Amount);
        Assert.Equal(500.13m, savedPayment.OriginalAmount);
        Assert.Equal(100.00m, savedPayment.PromoDiscountAmount);
    }

    [Fact]
    public async Task ConcurrentSameKeyRequestDoesNotSendSecondProviderRequest()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            Interlocked.Increment(ref requestCount);
            requestStarted.SetResult();
            await releaseProvider.Task;
            return await this.CreateProviderResponse(request, "provider-concurrent", "pending", false);
        });
        await using var firstContext = fixture.CreateContext();
        var firstRequest = this.CreateService(firstContext, firstHandler).CreatePaymentAsync(
            user.Id, plan.Id, 425, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var secondContext = fixture.CreateContext();
        var secondHandler = new StubHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var secondResult = await this.CreateService(secondContext, secondHandler).CreatePaymentAsync(
            user.Id, plan.Id, 425, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);
        releaseProvider.SetResult();
        var firstResult = await firstRequest;

        Assert.False(secondResult.IsSuccess);
        Assert.True(firstResult.IsSuccess, firstResult.ErrorMessage);
        Assert.Equal(1, requestCount);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(item => item.ExternalPaymentId == "provider-concurrent"));
    }

    [Fact]
    public async Task LocalFinalizeFailureCanRecoverWithSameContextAndProviderKey()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        const string externalPaymentId = "provider-local-finalize-retry";
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION "fail_payment_creation_test_insert"() RETURNS trigger AS $$
            BEGIN
                IF NEW."ExternalPaymentId" = 'provider-local-finalize-retry' THEN
                    RAISE EXCEPTION 'Не удалось сохранить тестовый платеж.';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER "fail_payment_creation_test_insert_trigger" BEFORE INSERT ON "SubscriptionPayments"
            FOR EACH ROW EXECUTE FUNCTION "fail_payment_creation_test_insert"();
            """);

        try
        {
            var handler = new StubHttpMessageHandler(request =>
                this.CreateProviderResponse(request, externalPaymentId, "pending", false));
            var service = this.CreateService(context, handler);
            var firstResult = await service.CreatePaymentAsync(
                user.Id, plan.Id, 475, "Тариф", "personal_subscription", this.IndividualBuyer,
                idempotenceKey: idempotenceKey);
            Assert.False(firstResult.IsSuccess);
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS "fail_payment_creation_test_insert_trigger" ON "SubscriptionPayments";
                DROP FUNCTION IF EXISTS "fail_payment_creation_test_insert"();
                """);
            await context.PaymentCreationOperations
                .Where(operation => operation.IdempotenceKey == idempotenceKey)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    operation => operation.LeaseUntil,
                    DateTime.UtcNow.AddMinutes(-1)));

            var retryResult = await service.CreatePaymentAsync(
                user.Id, plan.Id, 475, "Тариф", "personal_subscription", this.IndividualBuyer,
                idempotenceKey: idempotenceKey);

            var operation = await context.PaymentCreationOperations.AsNoTracking()
                .SingleAsync(item => item.IdempotenceKey == idempotenceKey);
            Assert.True(retryResult.IsSuccess, $"{retryResult.ErrorMessage}; {operation.Status}; {operation.LastErrorCode}");
            Assert.Equal(externalPaymentId, retryResult.PaymentId);
            await using var verification = fixture.CreateContext();
            Assert.Equal(1, await verification.PaymentCreationOperations.CountAsync(item => item.IdempotenceKey == idempotenceKey));
            Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(item => item.ExternalPaymentId == externalPaymentId));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS "fail_payment_creation_test_insert_trigger" ON "SubscriptionPayments";
                DROP FUNCTION IF EXISTS "fail_payment_creation_test_insert"();
                """);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleProviderResultCannotOverwriteNewerLease(bool staleResponseIsSuccess)
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var externalPaymentId = $"provider-stale-{Guid.NewGuid():N}";
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandler = new StubHttpMessageHandler(async request =>
        {
            requestStarted.SetResult();
            await releaseProvider.Task;
            return staleResponseIsSuccess
                ? await this.CreateProviderResponse(request, externalPaymentId, "pending", false)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        await using var firstContext = fixture.CreateContext();
        var firstRequest = this.CreateService(firstContext, firstHandler).CreatePaymentAsync(
            user.Id, plan.Id, 525, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using (var leaseContext = fixture.CreateContext())
        {
            await leaseContext.PaymentCreationOperations
                .Where(operation => operation.IdempotenceKey == idempotenceKey)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    operation => operation.LeaseUntil,
                    DateTime.UtcNow.AddMinutes(-1)));
        }

        var secondHandler = new StubHttpMessageHandler(request =>
            this.CreateProviderResponse(request, externalPaymentId, "pending", false));
        await using var secondContext = fixture.CreateContext();
        var secondResult = await this.CreateService(secondContext, secondHandler).CreatePaymentAsync(
            user.Id, plan.Id, 525, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);
        releaseProvider.SetResult();
        var firstResult = await firstRequest;

        await using var diagnostic = fixture.CreateContext();
        var diagnosticOperation = await diagnostic.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
        Assert.True(secondResult.IsSuccess, $"{secondResult.ErrorMessage}; {diagnosticOperation.Status}; {diagnosticOperation.LastErrorCode}");
        Assert.Equal(staleResponseIsSuccess, firstResult.IsSuccess);
        await using var verification = fixture.CreateContext();
        var savedOperation = await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
        Assert.Equal(PaymentCreationOperationStatuses.Created, savedOperation.Status);
        Assert.Equal(externalPaymentId, savedOperation.ExternalPaymentId);
        Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(item => item.ExternalPaymentId == externalPaymentId));
    }

    [Fact]
    public async Task InvalidProviderRequestIsTerminalForIdempotenceKey()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"code\":\"invalid_request\"}")
            });
        });

        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);
        var firstResult = await service.CreatePaymentAsync(
            user.Id, plan.Id, 550, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);
        var retryResult = await service.CreatePaymentAsync(
            user.Id, plan.Id, 550, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);

        Assert.False(firstResult.IsSuccess);
        Assert.False(retryResult.IsSuccess);
        Assert.Equal(1, requestCount);
        await using var verification = fixture.CreateContext();
        Assert.Equal(PaymentCreationOperationStatuses.Rejected,
            (await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey)).Status);
    }

    [Fact]
    public async Task ProviderIdentityMismatchRequiresManualReviewWithoutCreatingPayment()
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var handler = new StubHttpMessageHandler(async request =>
        {
            var response = await this.CreateProviderResponse(request, "provider-wrong-metadata", "pending", false);
            var content = await response.Content!.ReadAsStringAsync();
            response.Dispose();
            var tampered = content.Replace(user.Id, "different-user", StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(tampered, Encoding.UTF8, "application/json")
            };
        });

        await using var context = fixture.CreateContext();
        var result = await this.CreateService(context, handler).CreatePaymentAsync(
            user.Id, plan.Id, 575, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);

        Assert.False(result.IsSuccess);
        await using var verification = fixture.CreateContext();
        var saved = await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey);
        Assert.Equal(PaymentCreationOperationStatuses.ManualReview, saved.Status);
        Assert.False(await verification.SubscriptionPayments.AnyAsync(item => item.ExternalPaymentId == "provider-wrong-metadata"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ProviderAuthorizationFailureRequiresManualReview(HttpStatusCode statusCode)
    {
        var (user, plan) = await this.CreateOwnerAndPlanAsync();
        var idempotenceKey = Guid.NewGuid().ToString("N");
        var handler = new StubHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(statusCode)));
        await using var context = fixture.CreateContext();

        var result = await this.CreateService(context, handler).CreatePaymentAsync(
            user.Id, plan.Id, 600, "Тариф", "personal_subscription", this.IndividualBuyer,
            idempotenceKey: idempotenceKey);

        Assert.False(result.IsSuccess);
        await using var verification = fixture.CreateContext();
        Assert.Equal(PaymentCreationOperationStatuses.ManualReview,
            (await verification.PaymentCreationOperations.SingleAsync(item => item.IdempotenceKey == idempotenceKey)).Status);
    }

    private PaymentBuyerDto IndividualBuyer => new() { TypeCode = "individual" };

    private async Task<(ApplicationUser User, SubscriptionPlan Plan)> CreateOwnerAndPlanAsync()
    {
        var user = new ApplicationUser { Id = $"payment-operation-{Guid.NewGuid():N}" };
        var plan = new SubscriptionPlan
        {
            Name = "Проверка создания платежа",
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

    private async Task<int> CreatePromoCodeAsync()
    {
        var now = DateTime.UtcNow;
        var promoCode = new PromoCode
        {
            Code = $"PAY-{Guid.NewGuid():N}",
            Name = "Тестовый промокод",
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var context = fixture.CreateContext();
        context.PromoCodes.Add(promoCode);
        await context.SaveChangesAsync();

        return promoCode.Id;
    }

    private YooKassaService CreateService(OpenCRMContext context, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://payments.example.test/v3/") };
        this._httpClients.Add(httpClient);
        return new YooKassaService(
            httpClient,
            NullLogger<YooKassaService>.Instance,
            Options.Create(new YooKassaConfig { ShopId = "shop", SecretKey = "secret", ReturnUrl = "https://app.example.test" }),
            context);
    }

    private async Task<HttpResponseMessage> CreateProviderResponse(
        HttpRequestMessage request,
        string id,
        string status,
        bool paid,
        bool includeConfirmation = true)
    {
        using var requestJson = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var requestRoot = requestJson.RootElement;
        var amount = requestRoot.GetProperty("amount");
        var metadata = requestRoot.GetProperty("metadata");
        var response = new
        {
            id,
            status,
            paid,
            created_at = DateTime.UtcNow.ToString("O"),
            amount = new { value = amount.GetProperty("value").GetString(), currency = amount.GetProperty("currency").GetString() },
            confirmation = includeConfirmation ? new { type = "redirect", confirmation_url = "https://checkout.example.test/pay" } : null,
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
        public Task<string> RequestBodyAsync { get; private set; } = Task.FromResult(string.Empty);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestBodyAsync = request.Content!.ReadAsStringAsync(cancellationToken);
            return await send(request);
        }
    }
}
