using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCheckoutValidationTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PurchaseRejectsUnavailablePlanBeforeCalculatingPromotion()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var gateway = CreateGateway();
        var promotions = new Mock<IPromoCodeService>(MockBehavior.Strict);
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            promoCodeService: promotions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var result = await checkout.PurchaseAsync(organization.OwnerUserId, organization.Id, 999, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        gateway.Verify(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()), Times.Once);
        gateway.VerifyNoOtherCalls();
        promotions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("no-subscription")]
    [InlineData("demo")]
    [InlineData("default")]
    [InlineData("not-ready")]
    public async Task ExtensionRejectsIneligibleSubscription(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        if (scenario != "no-subscription")
        {
            var plan = await fixture.CreatePlanAsync(
                context,
                $"Тариф {scenario}",
                isDefault: scenario == "default",
                isDemo: scenario == "demo");
            var daysUntilExpiry = scenario == "not-ready" ? 14 : 2;
            await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(daysUntilExpiry));
        }

        var gateway = CreateGateway();
        var promotions = new Mock<IPromoCodeService>(MockBehavior.Strict);
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            promoCodeService: promotions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var result = await checkout.ExtendAsync(organization.OwnerUserId, organization.Id, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        gateway.Verify(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()), Times.Once);
        gateway.VerifyNoOtherCalls();
        promotions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DiscountIsFrozenInThePaymentDraft()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Тариф со скидкой");
        var discount = new PromotionPriceDto
        {
            OriginalPrice = plan.EffectivePrice,
            FinalPrice = plan.EffectivePrice - 100,
            DiscountAmount = 100,
            PromoCodeId = 42,
            PromoCode = "BILLING10"
        };
        var promotions = new Mock<IPromoCodeService>(MockBehavior.Strict);
        promotions.Setup(service => service.CalculatePriceAsync(
                organization.OwnerUserId, organization.Id, plan.Id, plan.EffectivePrice))
            .ReturnsAsync(discount);
        var gateway = CreateGateway();
        gateway.Setup(service => service.CreateAsync(It.IsAny<PaymentCreationDraft>()))
            .Returns((PaymentCreationDraft draft) =>
            {
                Assert.Equal(discount.FinalPrice, draft.Amount);
                Assert.Equal(discount.OriginalPrice, draft.Promotion?.OriginalAmount);
                Assert.Equal(discount.DiscountAmount, draft.Promotion?.DiscountAmount);
                Assert.Equal(discount.PromoCodeId, draft.Promotion?.PromoCodeId);
                Assert.Equal(discount.PromoCode, draft.Promotion?.PromoCode);

                return Task.FromResult(new PaymentCreationResult(
                    PaymentCreationStatus.PaymentRequired,
                    PaymentId: "discounted-payment",
                    PaymentUrl: "https://example.test/payment"));
            });
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            promoCodeService: promotions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var result = await checkout.PurchaseAsync(organization.OwnerUserId, organization.Id, plan.Id, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.PaymentRequired, result.Status);
        promotions.VerifyAll();
        gateway.VerifyAll();
    }

    private static Mock<IPaymentCreationGateway> CreateGateway()
    {
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ReturnsAsync((PaymentCreationResult?)null);

        return gateway;
    }
}
