using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminDashboardPeriodTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task DashboardPeriodCountsSentEmailsBySentAtAndFailuresAndSupportByCreationDate()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();
        var now = DateTime.UtcNow;
        var today = now.Date;
        var sevenDaysAgo = today.AddDays(-6);
        var thirtyDaysAgo = today.AddDays(-29);
        var beforeSevenDays = sevenDaysAgo.AddMilliseconds(-1);
        var beforeThirtyDays = thirtyDaysAgo.AddMilliseconds(-1);
        context.EmailMessages.AddRange(
            CreateEmail("old sent today", "delivered", today.AddHours(now.Hour), today.AddDays(-40)),
            CreateEmail("sent today", EmailMessageStatusCodes.Sent, today.AddHours(now.Hour), today),
            CreateEmail("sent at seven-day boundary", EmailMessageStatusCodes.Opened, sevenDaysAgo, sevenDaysAgo),
            CreateEmail("sent before seven days", EmailMessageStatusCodes.Clicked, beforeSevenDays, beforeSevenDays),
            CreateEmail("sent at thirty-day boundary", EmailMessageStatusCodes.Accepted, thirtyDaysAgo, thirtyDaysAgo),
            CreateEmail("sent before thirty days", EmailMessageStatusCodes.Sent, beforeThirtyDays, beforeThirtyDays),
            CreateEmail("simulated", EmailMessageStatusCodes.Simulated, today.AddHours(now.Hour), today),
            CreateEmail("failed today", EmailMessageStatusCodes.Failed, null, today, lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("failed at seven-day boundary", EmailMessageStatusCodes.Failed, null, sevenDaysAgo,
                lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("failed before seven days", EmailMessageStatusCodes.Failed, null, beforeSevenDays,
                lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("failed at thirty-day boundary", EmailMessageStatusCodes.Failed, null, thirtyDaysAgo,
                lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("failed before thirty days", EmailMessageStatusCodes.Failed, null, beforeThirtyDays,
                lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("old failure updated today", EmailMessageStatusCodes.Failed, null, today.AddDays(-40),
                lastEventAt: today.AddHours(now.Hour)),
            CreateEmail("pending old email", EmailMessageStatusCodes.Pending, null, today.AddDays(-40)),
            CreateEmail("future sent", EmailMessageStatusCodes.Sent, today.AddDays(1), today.AddDays(-40)),
            CreateEmail("future created", EmailMessageStatusCodes.Sent, today.AddDays(1), today.AddDays(1)));
        context.VehicleCatalogSuggestions.AddRange(
            CreateCatalogSuggestion("today", today),
            CreateCatalogSuggestion("seven-day boundary", sevenDaysAgo),
            CreateCatalogSuggestion("before seven days", beforeSevenDays),
            CreateCatalogSuggestion("thirty-day boundary", thirtyDaysAgo),
            CreateCatalogSuggestion("before thirty days", beforeThirtyDays),
            CreateCatalogSuggestion("future", today.AddDays(1)));
        context.SupportMessages.AddRange(
            CreateSupportMessage("today", today),
            CreateSupportMessage("seven-day boundary", sevenDaysAgo),
            CreateSupportMessage("before seven days", beforeSevenDays),
            CreateSupportMessage("thirty-day boundary", thirtyDaysAgo),
            CreateSupportMessage("before thirty days", beforeThirtyDays),
            CreateSupportMessage("future", today.AddDays(1)));
        await context.SaveChangesAsync();
        var service = new AdminDashboardService(
            context,
            new AdminDashboardCache(TimeProvider.System),
            TimeProvider.System);

        var oneDay = await service.GetAsync(1, canViewBilling: false);
        var sevenDays = await service.GetAsync(7, canViewBilling: false);
        var thirtyDays = await service.GetAsync(30, canViewBilling: false);

        Assert.Equal(2, oneDay.Period.SentEmails);
        Assert.Equal(3, oneDay.Period.Emails);
        Assert.Equal(1, oneDay.Period.CatalogSuggestions);
        Assert.Equal(1, oneDay.Period.FailedEmails);
        Assert.Equal(1, oneDay.Period.SupportMessages);
        Assert.Equal(3, sevenDays.Period.SentEmails);
        Assert.Equal(5, sevenDays.Period.Emails);
        Assert.Equal(2, sevenDays.Period.CatalogSuggestions);
        Assert.Equal(2, sevenDays.Period.FailedEmails);
        Assert.Equal(2, sevenDays.Period.SupportMessages);
        Assert.Equal(5, thirtyDays.Period.SentEmails);
        Assert.Equal(9, thirtyDays.Period.Emails);
        Assert.Equal(4, thirtyDays.Period.CatalogSuggestions);
        Assert.Equal(4, thirtyDays.Period.FailedEmails);
        Assert.Equal(4, thirtyDays.Period.SupportMessages);
        Assert.Equal(sevenDays.Period.SentEmails, sevenDays.Kpis.SentEmails7Days);
        Assert.Equal(2, sevenDays.Kpis.FailedEmails7Days);
        Assert.Equal(1, sevenDays.Kpis.PendingEmails);
        Assert.DoesNotContain(thirtyDays.Recent.Emails, message => message.Subject == "future created");
        Assert.DoesNotContain(thirtyDays.Recent.CatalogSuggestions, suggestion => suggestion.BrandName == "future");
        Assert.DoesNotContain(thirtyDays.Recent.SupportMessages, message => message.Topic == "future");
    }

    private static EmailMessage CreateEmail(
        string subject,
        string status,
        DateTime? sentAt,
        DateTime createdAt,
        DateTime? lastEventAt = null) =>
        new()
        {
            ToEmail = "contact@example.invalid",
            Subject = subject,
            StatusCode = status,
            SentAt = sentAt,
            CreatedAt = createdAt,
            LastEventAt = lastEventAt
        };

    private static SupportMessage CreateSupportMessage(string topic, DateTime createdAt) =>
        new()
        {
            Topic = topic,
            Text = topic,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static VehicleCatalogSuggestion CreateCatalogSuggestion(string brandName, DateTime createdAt) =>
        new()
        {
            Kind = VehicleKind.PassengerCar,
            BrandName = brandName,
            ModelName = "model",
            SuggestedByUserId = "dashboard-period-test",
            CreatedAt = createdAt,
            Status = VehicleCatalogSuggestionStatus.Pending
        };
}
