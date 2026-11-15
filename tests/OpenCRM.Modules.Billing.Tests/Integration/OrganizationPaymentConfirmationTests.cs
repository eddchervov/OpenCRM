using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationPaymentConfirmationTests(BillingDatabaseFixture fixture)
{
    [Theory]
    [InlineData("pending", false)]
    [InlineData("processing", false)]
    [InlineData("pending", true)]
    [InlineData("processing", true)]
    public async Task SucceededWebhookActivatesOnceAndRepeatedDeliveryDoesNotContactProvider(
        string status,
        bool extend)
    {
        await fixture.ResetDatabaseAsync();
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var userId = organization.OwnerUserId;
        var plan = await fixture.CreatePlanAsync(context, "Организационный платный");
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = oldEndDate.AddTicks(-(oldEndDate.Ticks % 10));
        if (extend)
        {
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id, SubscriptionPlanId = plan.Id,
                StartDate = oldEndDate.AddDays(-30), EndDate = oldEndDate, SourceCode = "paid"
            });
        }

        const string PaymentId = "organization-confirmation";
        var operation = $"yookassa_workspace_{(extend ? "extension" : "purchase")}:{organization.Id:D}";
        context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            UserId = userId, SubscriptionPlanId = plan.Id, Amount = plan.Price,
            ExternalPaymentId = PaymentId, PaymentMethod = operation, PaymentStatus = status,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var payments = new Mock<IYooKassaService>(MockBehavior.Strict);
        payments.Setup(service => service.GetLocalPaymentInfoAsync(PaymentId))
            .Returns(async () => await context.SubscriptionPayments.AsNoTracking()
                .Where(payment => payment.ExternalPaymentId == PaymentId)
                .Select(payment => new LocalPaymentInfoDto
                {
                    UserId = payment.UserId, PlanId = payment.SubscriptionPlanId,
                    Amount = payment.Amount, PaymentStatus = payment.PaymentStatus,
                    PaymentMethod = payment.PaymentMethod
                }).SingleAsync());
        payments.Setup(service => service.GetPaymentStatusAsync(PaymentId)).ReturnsAsync(new PaymentResponse
        {
            IsSuccess = true, PaymentId = PaymentId, Status = "succeeded",
            Paid = true, Currency = "RUB", Amount = plan.Price
        });
        payments.Setup(service => service.MarkPaymentPaidAsync(PaymentId)).Returns(async () =>
            await context.SubscriptionPayments.Where(payment => payment.ExternalPaymentId == PaymentId)
                .ExecuteUpdateAsync(update => update.SetProperty(payment => payment.PaidAt, DateTime.UtcNow)) == 1);
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton(context);
        registrations.AddSingleton(fixture.CreateDefaultsInitializer().Object);
        registrations.AddSingleton(fixture.CreatePromoCodeService().Object);
        registrations.AddScoped<IOrganizationAuditService, OrganizationAuditService>();
        registrations.AddScoped<IOrganizationPermissionService, OrganizationPermissionService>();
        registrations.AddSingleton(Mock.Of<IPaymentCreationGateway>());
        registrations.AddSingleton(Moq.Mock.Of<OpenCRM.Services.Interfaces.Payments.IYooKassaService>());
        registrations.AddSingleton(Mock.Of<IWorkspaceAccessService>());
        registrations.AddBillingExternalDependencies();
        registrations.AddOpenCRMBilling();
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        });
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();
        var personal = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var confirmation = new PaymentConfirmationService(activation, personal.Object, payments.Object, context);

        var webhook = new YooKassaWebhookService(confirmation, payments.Object, NullLogger<YooKassaWebhookService>.Instance);
        var notification = new YooKassaNotification { Event = "payment.succeeded", Object = new() { Id = PaymentId } };

        await webhook.HandleAsync(notification);
        await webhook.HandleAsync(notification);
        Assert.True(await confirmation.ConfirmAsync(PaymentId));
        await using var verification = fixture.CreateContext();
        var subscription = await verification.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(plan.Id, subscription.SubscriptionPlanId);
        Assert.Equal("paid", subscription.SourceCode);
        if (extend)
        {
            Assert.Equal(oldEndDate.AddDays(plan.DurationDays), subscription.EndDate);
        }

        Assert.Empty(await verification.UserSubscriptionHistory.ToListAsync());
        var audit = await verification.OrganizationAuditEvents.SingleAsync();
        Assert.Equal(organization.Id, audit.OrganizationId);
        Assert.Equal(userId, audit.ActorUserId);
        Assert.Equal(extend ? "billing.subscription.extended" : "billing.subscription.activated", audit.EventType);
        var payment = await verification.SubscriptionPayments.SingleAsync();
        Assert.Equal("completed", payment.PaymentStatus);
        Assert.NotNull(payment.PaidAt);
        Assert.NotNull(payment.StatusUpdatedAt);
        Assert.Equal("subscription-paid:" + PaymentId,
            (await verification.TelegramNotificationOutbox.SingleAsync()).NotificationKey);
        payments.Verify(service => service.GetPaymentStatusAsync(PaymentId), Times.Once);
        payments.Verify(service => service.MarkPaymentPaidAsync(PaymentId), Times.Once);
        personal.VerifyNoOtherCalls();
    }
}
