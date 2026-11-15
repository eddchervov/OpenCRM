using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalPaymentConfirmationTests(BillingDatabaseFixture fixture)
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
        var user = new ApplicationUser { Email = "personal-payment@example.test" };
        context.ApplicationUsers.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Личный платный");
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = oldEndDate.AddTicks(-(oldEndDate.Ticks % 10));
        if (extend)
        {
            context.UserSubscriptions.Add(new UserSubscription
            {
                UserId = user.Id, SubscriptionPlanId = plan.Id,
                StartDate = oldEndDate.AddDays(-30), EndDate = oldEndDate, SourceCode = "paid"
            });
        }

        const string PaymentId = "personal-confirmation";
        var operation = extend ? "yookassa_extension" : "yookassa_purchase";
        context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            UserId = user.Id, SubscriptionPlanId = plan.Id, Amount = plan.Price,
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
        using var services = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();
        var organizations = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        var confirmation = new PaymentConfirmationService(organizations.Object, activation, payments.Object, context);

        var webhook = new YooKassaWebhookService(confirmation, payments.Object, NullLogger<YooKassaWebhookService>.Instance);
        var notification = new YooKassaNotification { Event = "payment.succeeded", Object = new() { Id = PaymentId } };

        await webhook.HandleAsync(notification);
        await webhook.HandleAsync(notification);
        Assert.True(await confirmation.ConfirmAsync(PaymentId));
        await using var verification = fixture.CreateContext();
        var subscription = await verification.UserSubscriptions.SingleAsync();
        Assert.Equal(plan.Id, subscription.SubscriptionPlanId);
        Assert.Equal("paid", subscription.SourceCode);
        if (extend)
        {
            Assert.Equal(oldEndDate.AddDays(plan.DurationDays), subscription.EndDate);
        }

        var history = await verification.UserSubscriptionHistory.SingleAsync();
        Assert.Equal(extend ? "extended" : "created", history.Status);
        var payment = await verification.SubscriptionPayments.SingleAsync();
        Assert.Equal("completed", payment.PaymentStatus);
        Assert.NotNull(payment.PaidAt);
        Assert.NotNull(payment.StatusUpdatedAt);
        Assert.Equal("subscription-paid:" + PaymentId,
            (await verification.TelegramNotificationOutbox.SingleAsync()).NotificationKey);
        payments.Verify(service => service.GetPaymentStatusAsync(PaymentId), Times.Once);
        payments.Verify(service => service.MarkPaymentPaidAsync(PaymentId), Times.Once);
        organizations.VerifyNoOtherCalls();
    }
}
