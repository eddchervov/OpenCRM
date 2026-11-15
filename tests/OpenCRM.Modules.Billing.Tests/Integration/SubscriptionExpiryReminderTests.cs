using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class SubscriptionExpiryReminderTests(BillingDatabaseFixture fixture)
    : IClassFixture<BillingDatabaseFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SendDueRemindersIncludesPromoForDetailingAndServiceStationOnlyAndDeduplicates()
    {
        await using var context = fixture.CreateContext();
        var now = DateTime.UtcNow;
        var plan = await fixture.CreatePlanAsync(context, $"PRO-{Guid.NewGuid():N}");
        var expectedEmails = new List<(string Email, string Kind)>();
        var fleetEmails = new List<string>();

        foreach (var scenario in new[] { OrganizationTypeCodes.Detailing, OrganizationTypeCodes.ServiceStation })
        {
            foreach (var isExpired in new[] { false, true })
            {
                var organization = await fixture.CreateOrganizationAsync(context, scenario: scenario);
                var email = (await context.Users.FindAsync(organization.OwnerUserId))!.Email!;
                var endDate = isExpired ? now.AddDays(-1) : now.AddDays(1);
                context.OrganizationSubscriptions.Add(new OrganizationSubscription
                {
                    OrganizationId = organization.Id,
                    SubscriptionPlanId = plan.Id,
                    StartDate = endDate.AddDays(-30),
                    EndDate = endDate,
                    SourceCode = "promo"
                });
                expectedEmails.Add((email, isExpired ? "paid-expired" : "upcoming"));
            }
        }

        foreach (var isExpired in new[] { false, true })
        {
            var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
            var email = (await context.Users.FindAsync(organization.OwnerUserId))!.Email!;
            var endDate = isExpired ? now.AddDays(-1) : now.AddDays(1);
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlanId = plan.Id,
                StartDate = endDate.AddDays(-30),
                EndDate = endDate,
                SourceCode = "promo"
            });
            fleetEmails.Add(email);
        }

        var extendedOrganization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Detailing);
        var extendedEmail = (await context.Users.FindAsync(extendedOrganization.OwnerUserId))!.Email!;
        var expiredEndDate = now.AddDays(-1);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = extendedOrganization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = expiredEndDate.AddDays(-30),
            EndDate = expiredEndDate,
            SourceCode = "promo"
        });
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = extendedOrganization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = now,
            EndDate = now.AddDays(30),
            SourceCode = "paid"
        });

        var noAutoDemoEmails = new List<string>();
        foreach (var scenario in new[] { OrganizationTypeCodes.Detailing, OrganizationTypeCodes.ServiceStation })
        {
            var organization = await fixture.CreateOrganizationAsync(
                context,
                scenario: scenario,
                createdAt: now.AddDays(-2));
            noAutoDemoEmails.Add((await context.Users.FindAsync(organization.OwnerUserId))!.Email!);
        }

        await fixture.CreatePlanAsync(context, $"DEMO-{Guid.NewGuid():N}", isDemo: true);
        await context.SaveChangesAsync();

        var captured = new List<CapturedReminder>();
        var emailSender = new Mock<IEmailNotificationService>();
        emailSender
            .Setup(sender => sender.QueueTemplateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string?>(),
                It.IsAny<long?>()))
            .Callback<string, string, string, object, string?, long?>(
                (email, subject, _, data, _, _) => captured.Add(new CapturedReminder(
                    email,
                    subject,
                    JsonSerializer.SerializeToElement(data))))
            .Returns(Task.CompletedTask);
        var siteNotifications = new Mock<ISiteNotificationPublisher>();
        siteNotifications
            .Setup(publisher => publisher.AddAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(Task.CompletedTask);
        var service = new SubscriptionExpiryReminderService(
            context,
            emailSender.Object,
            siteNotifications.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Enabled"] = "true"
            }).Build(),
            NullLogger<SubscriptionExpiryReminderService>.Instance);

        await service.SendDueRemindersAsync();

        Assert.Equal(2, captured.Count(item => item.Data.GetProperty("kind").GetString() == "upcoming"));
        Assert.Equal(2, captured.Count(item => item.Data.GetProperty("kind").GetString() == "paid-expired"));
        Assert.DoesNotContain(captured, item => fleetEmails.Contains(item.Email));
        Assert.DoesNotContain(captured, item => item.Email == extendedEmail);
        Assert.DoesNotContain(captured, item => item.Data.GetProperty("kind").GetString() == "demo-available"
            && noAutoDemoEmails.Contains(item.Email));

        foreach (var expected in expectedEmails)
        {
            var reminder = Assert.Single(captured.Where(item => item.Email == expected.Email));
            Assert.Equal(expected.Kind, reminder.Data.GetProperty("kind").GetString());
            Assert.True(reminder.Data.GetProperty("hasFreeWorkspace").GetBoolean());
            Assert.Equal(
                expected.Kind == "upcoming"
                    ? "Срок коммерческого тарифа скоро закончится — работа без подписки останется доступной"
                    : "Коммерческий тариф завершён — продолжайте работу без подписки",
                reminder.Subject);
        }

        await service.SendDueRemindersAsync();

        Assert.Equal(4, captured.Count);
        Assert.Equal(4, await context.SubscriptionExpiryReminders.CountAsync());
    }

    private sealed record CapturedReminder(string Email, string Subject, JsonElement Data);
}
