using Moq;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class SubscriptionCheckoutTransactionTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProviderRunsAfterPreflightWithoutAnOuterDatabaseTransaction(bool organizationCheckout, bool extension)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Оплата без внешней транзакции");
        if (!organizationCheckout)
        {
            plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
            await context.SaveChangesAsync();
        }

        if (extension)
        {
            if (organizationCheckout)
            {
                await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(2));
            }
            else
            {
                context.UserSubscriptions.Add(new UserSubscription
                {
                    UserId = organization.OwnerUserId,
                    SubscriptionPlanId = plan.Id,
                    StartDate = DateTime.UtcNow.AddDays(-28),
                    EndDate = DateTime.UtcNow.AddDays(2),
                    SourceCode = "paid"
                });
                await context.SaveChangesAsync();
            }
        }

        var operationId = Guid.NewGuid();
        var buyer = new PaymentBuyerDto { TypeCode = "individual", Name = "Тестовый покупатель" };
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ReturnsAsync((PaymentCreationResult?)null);
        gateway.Setup(service => service.CreateAsync(It.IsAny<PaymentCreationDraft>()))
            .Returns((PaymentCreationDraft draft) =>
            {
                Assert.Null(context.Database.CurrentTransaction);
                Assert.False(context.ChangeTracker.HasChanges());
                Assert.Equal(operationId, draft.Intent.OperationId);
                Assert.Equal(organization.OwnerUserId, draft.Intent.UserId);
                Assert.Equal(plan.Id, draft.SubscriptionPlanId);
                Assert.Equal(plan.EffectivePrice, draft.Amount);
                Assert.Same(buyer, draft.Intent.Buyer);
                Assert.Equal(extension, draft.Intent.OperationType.Contains("extension", StringComparison.Ordinal));
                Assert.Equal(organizationCheckout, draft.Intent.OperationType.Contains("workspace", StringComparison.Ordinal));

                return Task.FromResult(new PaymentCreationResult(
                    PaymentCreationStatus.PaymentRequired,
                    PaymentId: "checkout-payment",
                    PaymentUrl: "https://example.test/payment"));
            });

        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: null,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();

        PaymentCreationResult result;
        if (organizationCheckout)
        {
            var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();
            result = extension
                ? await checkout.ExtendAsync(organization.OwnerUserId, organization.Id, buyer, operationId)
                : await checkout.PurchaseAsync(organization.OwnerUserId, organization.Id, plan.Id, buyer, operationId);
        }
        else
        {
            var checkout = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionCheckoutService>();
            result = extension
                ? await checkout.ExtendAsync(organization.OwnerUserId, buyer, operationId)
                : await checkout.PurchaseAsync(organization.OwnerUserId, plan.Id, buyer, operationId);
        }

        Assert.Equal(PaymentCreationStatus.PaymentRequired, result.Status);
        Assert.Equal("checkout-payment", result.PaymentId);
        gateway.VerifyAll();
    }
}
