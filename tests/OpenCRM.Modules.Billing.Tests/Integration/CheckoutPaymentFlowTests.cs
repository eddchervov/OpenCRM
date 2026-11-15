using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class CheckoutPaymentFlowTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FourCommandsRecoverStoredPaymentAfterLostResponseAndPricingChanges(bool workspace, bool extension)
    {
        await using var setup = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(setup);
        var plan = await fixture.CreatePlanAsync(setup, "Сохранённая цена");
        if (!workspace)
        {
            plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        }
        if (extension)
        {
            if (workspace)
            {
                await fixture.AddSubscriptionAsync(setup, organization, plan, DateTime.UtcNow.AddDays(2));
            }
            else
            {
                setup.UserSubscriptions.Add(new UserSubscription
                {
                    UserId = organization.OwnerUserId,
                    SubscriptionPlanId = plan.Id,
                    StartDate = DateTime.UtcNow.AddDays(-28),
                    EndDate = DateTime.UtcNow.AddDays(2),
                    SourceCode = "paid"
                });
            }
        }
        await setup.SaveChangesAsync();
        var promoCodeSnapshot = $"CHECKOUT-{Guid.NewGuid():N}";
        var promo = new PromoCode { Code = promoCodeSnapshot, Name = "Скидка", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        setup.PromoCodes.Add(promo);
        await setup.SaveChangesAsync();
        var operationId = Guid.NewGuid();
        var bodies = new List<string>();
        var keys = new List<string>();
        var attempts = 0;
        using var handler = new StubHttpHandler(async request =>
        {
            await using var verification = fixture.CreateContext();
            var stored = await verification.PaymentCreationOperations.AsNoTracking().SingleAsync();
            Assert.Equal(operationId.ToString("N"), stored.IdempotenceKey);
            bodies.Add(await request.Content!.ReadAsStringAsync());
            keys.Add(Assert.Single(request.Headers.GetValues("Idempotence-Key")));
            Assert.Equal(stored.RequestBody, bodies[^1]);
            if (++attempts == 1)
            {
                throw new HttpRequestException("Ответ потерян после создания платежа у провайдера.");
            }
            using var body = JsonDocument.Parse(bodies[^1]);
            var response = new
            {
                id = "stored-payment",
                status = "pending",
                paid = false,
                created_at = DateTime.UtcNow.ToString("O"),
                amount = body.RootElement.GetProperty("amount").Clone(),
                metadata = body.RootElement.GetProperty("metadata").Clone(),
                confirmation = new { type = "redirect", confirmation_url = "https://pay.example/stored" }
            };

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            };
        });

        async Task<PaymentCreationResult> CheckoutAsync()
        {
            await using var context = fixture.CreateContext();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(context);
            services.AddSingleton(Mock.Of<IPromotionPaymentCoordinator>());
            services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
            services.AddOpenCRMPaymentsInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["YooKassa:ShopId"] = "fake",
                ["YooKassa:SecretKey"] = "fake",
                ["YooKassa:ReturnUrl"] = "https://app.example"
            }).Build());
            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(factory => factory.CreateClient("OpenCRM.PaymentCreation"))
                .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://provider.example/") });
            services.AddSingleton(httpFactory.Object);
            using var payments = services.BuildServiceProvider();
            using var paymentScope = payments.CreateScope();
            var gateway = paymentScope.ServiceProvider.GetRequiredService<IPaymentCreationGateway>();
            var promotions = fixture.CreatePromoCodeService();
            if (workspace && attempts == 0)
            {
                promotions.Setup(service => service.CalculatePriceAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>()))
                    .ReturnsAsync(new PromotionPriceDto { OriginalPrice = 500, FinalPrice = 420, DiscountAmount = 80, PromoCodeId = promo.Id, PromoCode = promoCodeSnapshot });
            }
            if (attempts > 0)
            {
                promotions.Setup(service => service.CalculatePriceAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>()))
                    .ThrowsAsync(new InvalidOperationException("Повтор не должен заново рассчитывать промо."));
            }
            using var billing = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object,
                promoCodeService: promotions.Object, paymentCreationGateway: gateway);
            using var scope = billing.CreateScope();
            var buyer = new PaymentBuyerDto { TypeCode = "individual" };
            if (workspace)
            {
                var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

                return extension
                    ? await checkout.ExtendAsync(organization.OwnerUserId, organization.Id, buyer, operationId)
                    : await checkout.PurchaseAsync(organization.OwnerUserId, organization.Id, plan.Id, buyer, operationId);
            }
            var personal = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionCheckoutService>();

            return extension
                ? await personal.ExtendAsync(organization.OwnerUserId, buyer, operationId)
                : await personal.PurchaseAsync(organization.OwnerUserId, plan.Id, buyer, operationId);
        }

        Assert.Equal(PaymentCreationStatus.InProgress, (await CheckoutAsync()).Status);
        promo.IsActive = false;
        promo.Code = $"CHANGED-{Guid.NewGuid():N}";
        plan.Price = 900;
        plan.IsActive = false;
        await setup.SaveChangesAsync();
        await setup.OrganizationSubscriptions.ExecuteUpdateAsync(setters => setters.SetProperty(subscription => subscription.EndDate, DateTime.UtcNow.AddDays(30)));
        await setup.UserSubscriptions.ExecuteUpdateAsync(setters => setters.SetProperty(subscription => subscription.EndDate, DateTime.UtcNow.AddDays(30)));
        var recovered = await CheckoutAsync();
        Assert.Equal(PaymentCreationStatus.PaymentRequired, recovered.Status);
        Assert.Equal("stored-payment", recovered.PaymentId);
        Assert.Equal(2, attempts);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(keys[0], keys[1]);
        var payment = await setup.SubscriptionPayments.SingleAsync();
        Assert.Equal(workspace ? 420m : 500m, payment.Amount);
        Assert.Equal(workspace ? 500m : (decimal?)null, payment.OriginalAmount);
        Assert.Equal(workspace ? 80m : (decimal?)null, payment.PromoDiscountAmount);
        Assert.Equal(workspace ? promoCodeSnapshot : null, payment.PromoCodeSnapshot);
        Assert.Equal(1, await setup.PaymentCreationOperations.CountAsync());
        payment.PaymentStatus = "completed";
        await setup.SaveChangesAsync();
        Assert.Equal(PaymentCreationStatus.Completed, (await CheckoutAsync()).Status);
        Assert.Equal(2, attempts);
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
