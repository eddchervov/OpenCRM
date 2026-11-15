using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionCheckoutValidationTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("missing-plan")]
    [InlineData("inactive-plan")]
    [InlineData("demo-plan")]
    [InlineData("policy-rejected")]
    [InlineData("missing-user")]
    public async Task PurchaseRejectsInvalidRequestsBeforeCreatingPayment(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Личный тариф");
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        if (scenario == "inactive-plan")
        {
            plan.IsActive = false;
        }

        if (scenario == "demo-plan")
        {
            plan.IsDemoPlan = true;
        }

        await context.SaveChangesAsync();

        var gateway = CreateGateway();
        var policy = new Mock<IPersonalSubscriptionPurchasePolicyService>(MockBehavior.Strict);
        if (scenario == "missing-user")
        {
            policy.Setup(service => service.CanPurchasePlanAsync("missing-user", plan.Id))
                .ReturnsAsync(SubscriptionResult.CreateSuccess());
        }
        else if (scenario == "policy-rejected")
        {
            policy.Setup(service => service.CanPurchasePlanAsync(organization.OwnerUserId, plan.Id))
                .ReturnsAsync(SubscriptionResult.CreateFailed("Покупка запрещена."));
        }

        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            policy.Object,
            Mock.Of<IPersonalSubscriptionQueryService>(),
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);
        var userId = scenario == "missing-user" ? "missing-user" : organization.OwnerUserId;
        var requestedPlanId = scenario == "missing-plan" ? plan.Id + 1 : plan.Id;

        var result = await checkout.PurchaseAsync(userId, requestedPlanId, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        gateway.Verify(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()), Times.Once);
        gateway.VerifyNoOtherCalls();
        policy.VerifyAll();
    }

    [Theory]
    [InlineData("no-subscription")]
    [InlineData("demo-subscription")]
    [InlineData("not-ready")]
    public async Task ExtensionRejectsRequestsThatDoNotMeetSubscriptionRules(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var query = new Mock<IPersonalSubscriptionQueryService>(MockBehavior.Strict);
        if (scenario != "no-subscription")
        {
            var plan = await fixture.CreatePlanAsync(context, "Тариф для продления", isDemo: scenario == "demo-subscription");
            plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
            await context.SaveChangesAsync();
            context.UserSubscriptions.Add(new UserSubscription
            {
                UserId = organization.OwnerUserId,
                SubscriptionPlanId = plan.Id,
                StartDate = DateTime.UtcNow.AddDays(-30),
                EndDate = scenario == "not-ready" ? DateTime.UtcNow.AddDays(14) : DateTime.UtcNow.AddDays(2),
                SourceCode = "paid"
            });
            await context.SaveChangesAsync();

            if (scenario == "not-ready")
            {
                query.Setup(service => service.CanExtendSubscriptionAsync(organization.OwnerUserId)).ReturnsAsync(false);
            }
        }

        var gateway = CreateGateway();
        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>(),
            query.Object,
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);

        var result = await checkout.ExtendAsync(organization.OwnerUserId, new PaymentBuyerDto { TypeCode = "individual" }, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        gateway.Verify(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()), Times.Once);
        gateway.VerifyNoOtherCalls();
        query.VerifyAll();
    }

    private static Mock<IPaymentCreationGateway> CreateGateway()
    {
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ReturnsAsync((PaymentCreationResult?)null);

        return gateway;
    }
}
