using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Notifications;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class SiteNotificationServicesTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RegisteredPublisherUsesCallersContextAndLeavesCommitToCaller()
    {
        var user = new ApplicationUser();
        await using (var seed = fixture.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => fixture.CreateContext());
        services.AddOpenCRMNotifications(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalTesting:UseInMemoryNotifications"] = "true"
        }).Build());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRM.Infrastructure.Data.OpenCRMContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ISiteNotificationPublisher>();
        var key = Guid.NewGuid().ToString();

        await publisher.AddAsync(SiteNotificationTypes.Welcome, user.Id, 1, key, new Dictionary<string, string>());
        Assert.Contains(context.SiteNotifications.Local, item => item.EventKey == key);
        await using var verification = fixture.CreateContext();
        Assert.False(await verification.SiteNotifications.AnyAsync(item => item.EventKey == key));
        await publisher.AddRegistrationNotificationsAsync(user);
        Assert.True(await verification.SiteNotifications.AnyAsync(item => item.EventKey == key));
    }

    [Fact]
    public async Task PublisherDeduplicatesTrackedAndSavedEventsAndRendersLatestTemplate()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"{Guid.NewGuid():N}@example.com" };
        context.Users.Add(user);
        var template = new SiteNotification
        {
            IsTemplate = true,
            TypeCode = SiteNotificationTypes.Welcome,
            Title = "{name}",
            Body = "{name}"
        };
        context.SiteNotifications.Add(template);
        await context.SaveChangesAsync();
        var eventKey = Guid.NewGuid().ToString();
        var values = new Dictionary<string, string> { ["name"] = "Иван" + new string('a', 4010) };

        await SiteNotificationPublisher.AddAsync(context, SiteNotificationTypes.Welcome, user.Id, 1, eventKey, values);
        await SiteNotificationPublisher.AddAsync(context, SiteNotificationTypes.Welcome, user.Id, 1, eventKey, values);
        Assert.Single(context.SiteNotifications.Local, item => item.EventKey == eventKey);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await SiteNotificationPublisher.AddAsync(context, SiteNotificationTypes.Welcome, user.Id, 1, eventKey, values);
        Assert.Empty(context.SiteNotifications.Local);
        var saved = await context.SiteNotifications.SingleAsync(item => item.EventKey == eventKey);
        Assert.Equal(200, saved.Title.Length);
        Assert.Equal(4000, saved.Body.Length);
        Assert.StartsWith("Иван", saved.Title);
        Assert.StartsWith("Иван", saved.Body);
    }

    [Fact]
    public async Task RegistrationPublishesWelcomeAndOnlyPendingInvitationsForActiveOrganizations()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"{Guid.NewGuid():N}@example.com" };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        var organization = new Organization { Name = "Студия", OwnerUser = user };
        var invitation = new OrganizationInvitation
        {
            Organization = organization,
            InvitedByUser = user,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail
        };
        context.Users.Add(user);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();

        await SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user);
        await SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user);
        var notifications = await context.SiteNotifications.Where(item => item.RecipientUserId == user.Id).ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, item => item.TypeCode == SiteNotificationTypes.Welcome);
        Assert.Contains(notifications, item => item.TypeCode == SiteNotificationTypes.OrganizationInvitation && item.Body.Contains("Студия"));
        var noEmail = new ApplicationUser();
        context.Users.Add(noEmail);
        await context.SaveChangesAsync();
        await SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, noEmail);
        Assert.Single(await context.SiteNotifications.Where(item => item.RecipientUserId == noEmail.Id).ToListAsync());
    }

    [Fact]
    public async Task AudienceAndRecipientRulesAreAppliedBeforeIdempotentClosure()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var outsider = new ApplicationUser();
        context.Users.AddRange(owner, outsider);
        context.Organizations.Add(new Organization { OwnerUser = owner, Name = "Студия" });
        var notification = new SiteNotification { Title = "Владельцу", AudienceMask = SiteNotificationAudiences.OrganizationOwners, RecipientUserId = owner.Id };
        var admin = new SiteNotification { Title = "Администратору", AudienceMask = SiteNotificationAudiences.Admins };
        context.SiteNotifications.AddRange(notification, admin);
        await context.SaveChangesAsync();
        var service = new SiteNotificationService(context);

        Assert.Contains(await service.GetAsync(owner.Id, false, default), item => item.Id == notification.Id);
        Assert.DoesNotContain(await service.GetAsync(outsider.Id, false, default), item => item.Id == notification.Id);
        Assert.Contains(await service.GetAsync(outsider.Id, true, default), item => item.Id == admin.Id);
        Assert.False(await service.CloseAsync(outsider.Id, false, notification.Id, default));
        Assert.True(await service.CloseAsync(owner.Id, false, notification.Id, default));
        Assert.True(await service.CloseAsync(owner.Id, false, notification.Id, default));
        Assert.Single(await context.SiteNotificationClosures.Where(item => item.NotificationId == notification.Id).ToListAsync());
        Assert.NotNull((await service.GetAsync(owner.Id, false, default)).Single(item => item.Id == notification.Id).ClosedAt);
    }

    [Fact]
    public async Task NotificationsBeforeRegistrationAreOnlyAvailableToTheirRecipient()
    {
        await using var context = fixture.CreateContext();
        var registeredAt = new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser { DatetimeCreation = registeredAt };
        var earlierUser = new ApplicationUser { DatetimeCreation = registeredAt.AddDays(-1) };

        var announcement = new SiteNotification
        {
            Title = "Общее объявление до регистрации",
            AudienceMask = SiteNotificationAudiences.All,
            CreatedAt = registeredAt.AddSeconds(-1)
        };

        var personal = new SiteNotification
        {
            Title = "Личное уведомление",
            AudienceMask = SiteNotificationAudiences.All,
            RecipientUserId = user.Id,
            CreatedAt = registeredAt.AddSeconds(-1)
        };

        context.Users.AddRange(user, earlierUser);
        context.SiteNotifications.AddRange(announcement, personal);
        await context.SaveChangesAsync();
        var service = new SiteNotificationService(context);

        var notifications = await service.GetAsync(user.Id, false, default);

        Assert.Equal(personal.Id, Assert.Single(notifications).Id);
        Assert.Equal(announcement.Id, Assert.Single(await service.GetAsync(earlierUser.Id, false, default)).Id);
        Assert.False(await service.CloseAsync(user.Id, false, announcement.Id, default));
        Assert.True(await service.CloseAsync(user.Id, false, personal.Id, default));
        Assert.False(await context.SiteNotificationClosures.AnyAsync(closure => closure.NotificationId == announcement.Id));
    }

    [Theory]
    [InlineData(SiteNotificationAudiences.All)]
    [InlineData(SiteNotificationAudiences.Admins)]
    [InlineData(SiteNotificationAudiences.OrganizationOwners)]
    public async Task BroadcastsAreAvailableFromRegistrationTimeForEveryAudience(int audienceMask)
    {
        await using var context = fixture.CreateContext();
        var registeredAt = new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser { DatetimeCreation = registeredAt };
        context.Organizations.Add(new Organization { OwnerUser = user, Name = "Студия" });

        var before = new SiteNotification
        {
            Title = "До регистрации",
            AudienceMask = audienceMask,
            CreatedAt = registeredAt.AddSeconds(-1)
        };

        var atRegistration = new SiteNotification
        {
            Title = "В момент регистрации",
            AudienceMask = audienceMask,
            CreatedAt = registeredAt
        };

        var after = new SiteNotification
        {
            Title = "После регистрации",
            AudienceMask = audienceMask,
            CreatedAt = registeredAt.AddSeconds(1)
        };

        context.SiteNotifications.AddRange(before, atRegistration, after);
        await context.SaveChangesAsync();
        var service = new SiteNotificationService(context);

        var notifications = await service.GetAsync(user.Id, true, default);

        Assert.Equal(new[] { after.Id, atRegistration.Id }, notifications.Select(notification => notification.Id));
        Assert.False(await service.CloseAsync(user.Id, true, before.Id, default));
        Assert.True(await service.CloseAsync(user.Id, true, atRegistration.Id, default));
        Assert.True(await service.CloseAsync(user.Id, true, after.Id, default));
    }

    [Fact]
    public async Task AdministratorCanOverrideDefaultTemplatesAndArchiveThem()
    {
        await using var context = fixture.CreateContext();
        var service = new AdminSiteNotificationService(context);
        var templates = await service.GetAsync(default);
        var welcome = templates.Single(item => item.TypeCode == SiteNotificationTypes.InvitationAccepted && item.IsTemplate);

        Assert.Null(await service.UpdateTemplateAsync(Guid.NewGuid(), "a", "b", default));
        Assert.NotNull(await service.UpdateTemplateAsync(welcome.Id, "Привет", "Текст", default));
        Assert.NotNull(await service.UpdateTemplateAsync(welcome.Id, "Привет снова", "Текст", default));
        Assert.Contains(await service.GetAsync(default), item => item.Id == welcome.Id && item.Title == "Привет снова");
        Assert.True(await service.TryDeleteAsync(welcome.Id, default));
        Assert.False(await service.TryDeleteAsync(welcome.Id, default));
        Assert.Empty(await service.SearchUsersAsync(null, default));
        var user = new ApplicationUser { Email = $"search-{Guid.NewGuid():N}@example.com" };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        Assert.Contains(await service.SearchUsersAsync(user.Email, default), item => item.Id == user.Id);
    }

    [Theory]
    [InlineData("invalid", null, null)]
    [InlineData("all", null, null)]
    [InlineData("all", "invalid", null)]
    [InlineData("selected", "all", null)]
    [InlineData("all", "all", "missing")]
    [InlineData("selected", "all", "missing")]
    public async Task InvalidRecipientSelectionsDoNotCreateNotifications(string scope, string? audience, string? userId)
    {
        await using var context = fixture.CreateContext();
        var service = new AdminSiteNotificationService(context);
        var request = new AdminSiteNotificationSendDto
        {
            RecipientScope = scope,
            Audiences = audience == null ? null : [audience],
            UserIds = userId == null ? null : [userId]
        };

        Assert.NotNull(await service.SendAsync(request, default));
        Assert.Empty(context.SiteNotifications.Local);
    }

    [Fact]
    public async Task SelectedUsersMustBelongToAudienceAndDuplicatesProduceOneMessage()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new AdminSiteNotificationService(context);
        Assert.NotNull(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            RecipientScope = "selected", Audiences = ["admins"], UserIds = [user.Id]
        }, default));
        Assert.Null(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            Title = "Личное", RecipientScope = "selected", Audiences = ["all"], UserIds = [user.Id, user.Id]
        }, default));
        Assert.Single(await context.SiteNotifications.Where(item => item.RecipientUserId == user.Id && item.Title == "Личное").ToListAsync());
        Assert.Null(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            Title = "Общее", RecipientScope = "all", Audiences = ["admins", "organization-owners"]
        }, default));
        Assert.Contains(context.SiteNotifications.Local, item => item.Title == "Общее" && item.AudienceMask == 6);
    }

    [Fact]
    public async Task CombinedAllAudienceAndOversizedRecipientListsAreRejected()
    {
        await using var context = fixture.CreateContext();
        var service = new AdminSiteNotificationService(context);

        Assert.NotNull(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            RecipientScope = "all", Audiences = ["all", "admins"]
        }, default));
        Assert.NotNull(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            RecipientScope = "selected", Audiences = ["all"],
            UserIds = Enumerable.Range(0, 501).Select(index => index.ToString()).ToArray()
        }, default));
        Assert.Empty(await service.SearchUsersAsync("  ", default));
        Assert.Empty(context.SiteNotifications.Local);
    }

    [Fact]
    public async Task SelectedOwnerReceivesOwnerAudienceMessage()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        context.Organizations.Add(new Organization { OwnerUser = owner, Name = "Студия" });
        await context.SaveChangesAsync();
        var service = new AdminSiteNotificationService(context);

        Assert.Null(await service.SendAsync(new AdminSiteNotificationSendDto
        {
            Title = "Владельцу", RecipientScope = "selected", Audiences = ["organization-owners"], UserIds = [owner.Id]
        }, default));
        Assert.Equal(SiteNotificationAudiences.OrganizationOwners,
            (await context.SiteNotifications.SingleAsync(item => item.RecipientUserId == owner.Id)).AudienceMask);
    }
}
