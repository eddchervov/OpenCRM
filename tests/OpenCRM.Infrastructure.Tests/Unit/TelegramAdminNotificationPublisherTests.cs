using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Notifications;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class TelegramAdminNotificationPublisherTests
{
    [Fact]
    public void RegistrationQueuesUserDetailsWithStableKey()
    {
        using var context = CreateContext();
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "contact@example.invalid",
            PhoneNumber = "+7 000 000-00-01"
        };

        TelegramAdminNotificationPublisher.AddRegistration(context, user);

        var message = Assert.Single(context.TelegramNotificationOutbox.Local);
        Assert.Equal("registration:user-1", message.NotificationKey);
        Assert.Contains("contact@example.invalid", message.Body);
        Assert.Contains("+7 000 000-00-01", message.Body);
        Assert.Contains("ID: user-1", message.Body);
        Assert.DoesNotContain("Тип аккаунта", message.Body);
    }

    [Fact]
    public void PurchaseQueuesPaymentDetailsWithStableKey()
    {
        using var context = CreateContext();
        var organizationId = Guid.NewGuid();

        TelegramAdminNotificationPublisher.AddPurchase(
            context, "user-1", "contact@example.invalid", "Бизнес", 1234.50m, "payment-1", organizationId);

        var message = Assert.Single(context.TelegramNotificationOutbox.Local);
        Assert.Equal("subscription-paid:payment-1", message.NotificationKey);
        Assert.Contains(organizationId.ToString(), message.Body);
        Assert.Contains("Бизнес", message.Body);
        Assert.Contains("contact@example.invalid", message.Body);
    }

    private static OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        return new OpenCRMContext(options);
    }
}
