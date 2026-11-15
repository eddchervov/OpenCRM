using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminNavigationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task NavigationReadStateMigrationRollsBackWithoutRemovingImportTables()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20261015130415_FixVehicleCatalogSuggestionUniqueIndex");

        var afterDown = await GetNavigationAndImportTableExistenceAsync(context);
        Assert.False(afterDown.NavigationReadStatesExists);
        Assert.True(afterDown.OrganizationImportDraftsExists);

        await migrator.MigrateAsync(null);

        var afterUp = await GetNavigationAndImportTableExistenceAsync(context);
        Assert.True(afterUp.NavigationReadStatesExists);
        Assert.True(afterUp.OrganizationImportDraftsExists);
    }

    [Fact]
    public async Task UnreadCountsSeedOncePersistAcrossDaysAndRemainIsolatedByAdministrator()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();
        await context.VehicleCatalogSuggestions.ExecuteDeleteAsync();
        var baselineUtc = DateTime.UtcNow.Date.AddDays(-2);
        var dayAfterBaselineUtc = baselineUtc.AddDays(1);
        var todayUtc = DateTime.UtcNow.Date;
        var todayEventUtc = DateTime.UtcNow.AddMinutes(-1);
        var firstAdminId = Guid.NewGuid().ToString();
        var secondAdminId = Guid.NewGuid().ToString();
        context.ApplicationUsers.AddRange(
            new ApplicationUser { Id = firstAdminId, DatetimeCreation = baselineUtc, IsDeleted = true },
            new ApplicationUser { Id = secondAdminId, DatetimeCreation = baselineUtc, IsDeleted = true });

        var plan = new SubscriptionPlan
        {
            Name = $"Проверка отметок {Guid.NewGuid():N}",
            DurationDays = 30
        };
        context.SubscriptionPlans.Add(plan);
        var eventDates = new[]
        {
            baselineUtc.AddHours(1),
            dayAfterBaselineUtc.AddHours(1),
            dayAfterBaselineUtc.AddHours(3),
            todayEventUtc
        };

        foreach (var date in eventDates)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), DatetimeCreation = date };
            context.ApplicationUsers.Add(user);
            context.SubscriptionPayments.Add(new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = date,
                PaidAt = date,
                Amount = 100,
                PaymentStatus = "pending"
            });
            context.SupportMessages.Add(new SupportMessage
            {
                Topic = "Проверка",
                Text = "Проверка непрочитанных счётчиков",
                CreatedAt = date,
                UpdatedAt = todayEventUtc
            });
            context.EmailMessages.Add(new EmailMessage
            {
                ToEmail = "contact@example.invalid",
                Subject = "Проверка",
                CreatedAt = date,
                SentAt = todayEventUtc
            });
        }

        context.VehicleCatalogSuggestions.Add(new VehicleCatalogSuggestion
        {
            BrandName = "Проверка",
            ModelName = "На модерации",
            SuggestedByUserId = firstAdminId,
            CreatedAt = baselineUtc,
            Status = VehicleCatalogSuggestionStatus.Pending
        });
        await context.SaveChangesAsync();

        var permissions = new HashSet<string>
        {
            AdminAuthorizationPolicies.ViewUsers,
            AdminAuthorizationPolicies.ViewBilling,
            AdminAuthorizationPolicies.ViewContent,
            AdminAuthorizationPolicies.ViewCommunications
        };
        var service = new AdminNavigationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build());
        var firstCounts = await service.GetUnreadCountsAsync(firstAdminId, baselineUtc, permissions);
        var nextDayCounts = await service.GetUnreadCountsAsync(firstAdminId, dayAfterBaselineUtc.AddHours(2), permissions);

        Assert.Equal(4, firstCounts.Users);
        Assert.Equal(4, nextDayCounts.Users);
        Assert.Equal(4, nextDayCounts.Payments);
        Assert.Equal(4, nextDayCounts.SupportMessages);
        Assert.Equal(4, nextDayCounts.Emails);
        Assert.Equal(1, nextDayCounts.PendingCatalogSuggestions);
        Assert.NotNull(nextDayCounts.AsOfUtc);
        Assert.Equal(0, nextDayCounts.AsOfUtc.Value.Ticks % 10);

        var viewedThroughUtc = dayAfterBaselineUtc.AddHours(2);
        foreach (var sectionCode in new[]
                 {
                     AdminNavigationSections.Users,
                     AdminNavigationSections.SupportMessages,
                     AdminNavigationSections.Emails
                 })
        {
            await service.MarkSectionViewedAsync(firstAdminId, sectionCode, viewedThroughUtc);
        }

        var afterViewing = await service.GetUnreadCountsAsync(firstAdminId, todayUtc, permissions);
        var secondAdminCounts = await service.GetUnreadCountsAsync(secondAdminId, todayUtc, permissions);

        Assert.Equal(2, afterViewing.Users);
        Assert.Equal(4, afterViewing.Payments);
        Assert.Equal(2, afterViewing.SupportMessages);
        Assert.Equal(2, afterViewing.Emails);
        Assert.Equal(1, afterViewing.PendingCatalogSuggestions);
        Assert.Equal(1, secondAdminCounts.Users);
        Assert.Equal(4, secondAdminCounts.Payments);
        Assert.Equal(1, secondAdminCounts.SupportMessages);
        Assert.Equal(1, secondAdminCounts.Emails);

        var restricted = await service.GetUnreadCountsAsync(
            secondAdminId, todayUtc, new HashSet<string> { AdminAuthorizationPolicies.ViewUsers });

        Assert.Equal(1, restricted.Users);
        Assert.Equal(0, restricted.Payments);
        Assert.Equal(0, restricted.SupportMessages);
        Assert.Equal(0, restricted.Emails);
        Assert.Equal(0, restricted.PendingCatalogSuggestions);
    }

    [Fact]
    public async Task UnreadPaymentsRemainUntilPaidTransactionsAreProcessedAndHaveReceipts()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();
        var oldUtc = DateTime.UtcNow.AddYears(-2);
        var recentUtc = DateTime.UtcNow.AddMinutes(-1);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            DatetimeCreation = oldUtc
        };
        var plan = new SubscriptionPlan { Name = $"Чеки {Guid.NewGuid():N}", DurationDays = 30 };
        context.ApplicationUsers.Add(user);
        context.SubscriptionPlans.Add(plan);
        context.SubscriptionPayments.AddRange(
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = oldUtc,
                PaidAt = oldUtc,
                Amount = 100,
                PaymentStatus = "pending"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "failed",
                NpdReceiptFilePath = "receipts/failed.pdf"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "completed"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "completed",
                NpdReceiptFilePath = string.Empty
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "completed",
                NpdReceiptUrl = "  "
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "completed",
                NpdReceiptFilePath = "receipts/completed.pdf"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "completed",
                NpdReceiptUrl = "https://example.test/receipt"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                Amount = 100,
                PaymentStatus = "pending"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                RefundedAmount = 100,
                PaymentStatus = "refunded"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                PaymentStatus = "canceled"
            },
            new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = recentUtc,
                PaidAt = recentUtc,
                Amount = 100,
                RefundedAmount = 25,
                PaymentStatus = "completed"
            });
        await context.SaveChangesAsync();

        var counts = await new AdminNavigationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build()).GetUnreadCountsAsync(
            Guid.NewGuid().ToString(),
            DateTime.UtcNow,
            new HashSet<string> { AdminAuthorizationPolicies.ViewBilling });

        Assert.Equal(6, counts.Payments);
    }

    [Fact]
    public async Task ConcurrentReadMarkersNeverMoveBackwards()
    {
        await fixture.ResetAsync();
        var userId = Guid.NewGuid().ToString();
        var baselineUtc = DateTime.UtcNow.Date.AddDays(-1);
        await using (var context = fixture.CreateContext())
        {
            context.ApplicationUsers.Add(new ApplicationUser
            {
                Id = userId,
                DatetimeCreation = baselineUtc,
                IsDeleted = true
            });
            await context.SaveChangesAsync();
        }

        var permissions = new HashSet<string> { AdminAuthorizationPolicies.ViewUsers };
        await using (var context = fixture.CreateContext())
        {
            await new AdminNavigationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build()).GetUnreadCountsAsync(userId, baselineUtc, permissions);
        }

        var earlierUtc = baselineUtc.AddHours(6);
        var laterUtc = baselineUtc.AddHours(12);
        await Task.WhenAll(
            MarkViewedAsync(userId, laterUtc),
            MarkViewedAsync(userId, earlierUtc));

        await using var verificationContext = fixture.CreateContext();
        var state = await verificationContext.AdminNavigationReadStates.SingleAsync(
            readState => readState.UserId == userId && readState.SectionCode == AdminNavigationSections.Users);

        Assert.Equal(laterUtc, state.ViewedThroughUtc);
    }

    private async Task MarkViewedAsync(string userId, DateTime viewedThroughUtc)
    {
        await using var context = fixture.CreateContext();
        await new AdminNavigationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build()).MarkSectionViewedAsync(
            userId, AdminNavigationSections.Users, viewedThroughUtc);
    }

    private static async Task<(bool NavigationReadStatesExists, bool OrganizationImportDraftsExists)>
        GetNavigationAndImportTableExistenceAsync(OpenCRMContext context)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                to_regclass('admin."NavigationReadStates"') IS NOT NULL,
                to_regclass('"OrganizationImportDrafts"') IS NOT NULL
            """;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    [Fact]
    public async Task CountsUseLocalDayBoundariesAndRespectSectionPermissions()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();
        await context.VehicleCatalogSuggestions.ExecuteDeleteAsync();
        var fromUtc = new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddDays(1);
        var plan = new SubscriptionPlan { Name = "Проверка счётчиков", DurationDays = 30 };
        context.SubscriptionPlans.Add(plan);

        foreach (var date in new[] { fromUtc.AddTicks(-10), fromUtc, toUtc.AddTicks(-10), toUtc })
        {
            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), DatetimeCreation = date };
            context.ApplicationUsers.Add(user);
            context.SubscriptionPayments.Add(new SubscriptionPayment
            {
                User = user,
                SubscriptionPlan = plan,
                CreatedAt = date,
                PaymentStatus = "pending"
            });
            context.SupportMessages.Add(new SupportMessage
            {
                Topic = "Проверка",
                Text = "Проверка границ дня",
                CreatedAt = date,
                UpdatedAt = toUtc
            });
            context.EmailMessages.Add(new EmailMessage
            {
                ToEmail = "contact@example.invalid",
                Subject = "Проверка",
                CreatedAt = date,
                SentAt = toUtc
            });
        }

        context.ApplicationUsers.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), DatetimeCreation = fromUtc, IsDeleted = true
        });
        foreach (var (index, date) in new[] { fromUtc.AddDays(-20), fromUtc, toUtc }.Index())
        {
            context.VehicleCatalogSuggestions.Add(new VehicleCatalogSuggestion
            {
                BrandName = "Проверка",
                ModelName = $"Модель {index}",
                SuggestedByUserId = "test-user",
                CreatedAt = date,
                Status = VehicleCatalogSuggestionStatus.Pending
            });
        }

        context.VehicleCatalogSuggestions.Add(new VehicleCatalogSuggestion
        {
            BrandName = "Проверка",
            ModelName = "Одобрено",
            SuggestedByUserId = "test-user",
            CreatedAt = fromUtc,
            Status = VehicleCatalogSuggestionStatus.Approved
        });
        await context.SaveChangesAsync();
        var service = new AdminNavigationService(context, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PlatformPolicy:Commercial:Enabled"] = "true" }).Build());
        var permissions = new HashSet<string>
        {
            AdminAuthorizationPolicies.ViewUsers,
            AdminAuthorizationPolicies.ViewBilling,
            AdminAuthorizationPolicies.ViewContent,
            AdminAuthorizationPolicies.ViewCommunications
        };

        var counts = await service.GetCountsAsync(fromUtc, toUtc, permissions);

        Assert.Equal(2, counts.Users);
        Assert.Equal(2, counts.Payments);
        Assert.Equal(2, counts.SupportMessages);
        Assert.Equal(2, counts.Emails);
        Assert.Equal(3, counts.PendingCatalogSuggestions);

        var restricted = await service.GetCountsAsync(fromUtc, toUtc,
            new HashSet<string> { AdminAuthorizationPolicies.ViewContent });

        Assert.Equal(0, restricted.Users);
        Assert.Equal(0, restricted.Payments);
        Assert.Equal(2, restricted.SupportMessages);
        Assert.Equal(0, restricted.Emails);
        Assert.Equal(3, restricted.PendingCatalogSuggestions);

        var withoutContentAccess = await service.GetCountsAsync(fromUtc, toUtc,
            new HashSet<string> { AdminAuthorizationPolicies.ViewUsers });
        Assert.Equal(0, withoutContentAccess.PendingCatalogSuggestions);

        var tomorrow = await service.GetCountsAsync(toUtc, toUtc.AddDays(1), permissions);

        Assert.Equal(1, tomorrow.Users);
        Assert.Equal(1, tomorrow.Payments);
        Assert.Equal(1, tomorrow.SupportMessages);
        Assert.Equal(1, tomorrow.Emails);
        Assert.Equal(3, tomorrow.PendingCatalogSuggestions);
    }
}
