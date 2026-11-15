using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class CommercialBackgroundJobsTests
{
    [Fact]
    public async Task DisabledExpiryDoesNotQueryOrChangeSubscriptions()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=test;Password=test").Options);
        var service = new PersonalSubscriptionCommandService(context,
            NullLogger<PersonalSubscriptionCommandService>.Instance, Mock.Of<IPlatformCommercialPolicy>());

        await service.CheckAndDeactivateExpiredSubscriptionsAsync();

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task DisabledRemindersDoNotQueryDataOrSendMessages()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=test;Password=test").Options);
        var email = new Mock<IEmailNotificationService>(MockBehavior.Strict);
        var notifications = new Mock<ISiteNotificationPublisher>(MockBehavior.Strict);
        var service = new SubscriptionExpiryReminderService(context, email.Object, notifications.Object,
            new ConfigurationBuilder().Build(), NullLogger<SubscriptionExpiryReminderService>.Instance);

        await service.SendDueRemindersAsync();

        email.VerifyNoOtherCalls();
        notifications.VerifyNoOtherCalls();
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyQueuedReconciliationRespectsTheCurrentMode(bool enabled)
    {
        var automation = new Mock<IBillingAutomationService>(MockBehavior.Strict);
        if (enabled)
        {
            automation.Setup(service => service.ReconcilePaymentsAsync()).Returns(Task.CompletedTask);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = enabled.ToString()
        }).Build();
        var job = new OpenCRM.WebApp.Infrastructure.Billing.BillingAutomationService(automation.Object, configuration);

        await job.ReconcilePaymentsAsync();

        automation.Verify(service => service.ReconcilePaymentsAsync(), enabled ? Times.Once() : Times.Never());
        automation.VerifyNoOtherCalls();
    }
}
