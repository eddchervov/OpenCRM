using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Accounts;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class AccountRegistrationTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    private const string Password ="Registration-test-123!";

    [Fact]
    public async Task CreateAsync_WithPassword_CreatesUserWithConsents()
    {
        await using var context = fixture.CreateContext();
        var email = $"registration-ok-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);

        var result = await harness.Service.CreateAsync(new AccountRegistrationRequest(
            email,
            Password,
            PersonalDataProcessingConsent: true,
            AdvertisingConsent: false,
            Attribution: null));

        Assert.Empty(result.Errors);
        var user = Assert.IsType<ApplicationUser>(result.User);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.PersonalDataProcessingConsentAccepted);
        Assert.NotNull(user.PersonalDataProcessingConsentAcceptedAt);
        Assert.False(user.AdvertisingConsentAccepted);
        Assert.Null(user.AdvertisingConsentAcceptedAt);

        harness.Notifications.Verify(
            publisher => publisher.AddRegistrationNotificationsAsync(It.Is<ApplicationUser>(created => created.Id == user.Id)),
            Times.Once);

        // Строка Telegram-уведомления о новой регистрации.
        var outbox = await context.TelegramNotificationOutbox.AsNoTracking()
            .SingleAsync(item => item.NotificationKey == $"registration:{user.Id}");
        Assert.Contains(email, outbox.Body);
    }

    [Fact]
    public async Task CreateAsync_WithoutPassword_ConfirmsEmail()
    {
        await using var context = fixture.CreateContext();
        var email = $"registration-nopass-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);

        var result = await harness.Service.CreateAsync(new AccountRegistrationRequest(
            email,
            Password: null,
            PersonalDataProcessingConsent: true,
            AdvertisingConsent: true,
            Attribution: null));

        Assert.Empty(result.Errors);
        Assert.True(result.User!.EmailConfirmed);
        Assert.True(result.User.AdvertisingConsentAccepted);
        Assert.NotNull(result.User.AdvertisingConsentAcceptedAt);
    }

    [Fact]
    public async Task CreateAsync_WithAttribution_AppliesUtmFields()
    {
        await using var context = fixture.CreateContext();
        var email = $"registration-utm-{Guid.NewGuid():N}@example.com";
        await using var harness = CreateService(context);
        var attribution = new RegistrationAttributionData
        {
            FirstUtmSource = "test-source",
            FirstUtmMedium = "test-medium",
            FirstUtmCampaign = "test-campaign",
            FirstVisitAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUtmSource = "last-source",
            LastUtmMedium = "last-medium",
            LastUtmCampaign = "last-campaign",
            LastVisitAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = await harness.Service.CreateAsync(new AccountRegistrationRequest(
            email,
            Password,
            PersonalDataProcessingConsent: true,
            AdvertisingConsent: false,
            Attribution: attribution));

        var user = Assert.IsType<ApplicationUser>(result.User);
        // Атрибуция перенесена в поля пользователя
        Assert.Equal("test-source", user.FirstUtmSource);
        Assert.Equal("test-medium", user.FirstUtmMedium);
        Assert.Equal("test-campaign", user.FirstUtmCampaign);
        Assert.Equal(attribution.FirstVisitAt, user.FirstVisitAt);
        Assert.Equal("last-source", user.LastUtmSource);
        Assert.Equal("last-medium", user.LastUtmMedium);
        Assert.Equal("last-campaign", user.LastUtmCampaign);
        Assert.Equal(attribution.LastVisitAt, user.LastVisitAt);
    }

    [Fact]
    public async Task CreateAsync_WithCreationFailure_DoesNotSendNotifications()
    {
        await using var context = fixture.CreateContext();
        var email = $"registration-dup-{Guid.NewGuid():N}@example.com";
        await using var first = CreateService(context);

        var created = await first.Service.CreateAsync(new AccountRegistrationRequest(
            email,
            Password,
            PersonalDataProcessingConsent: true,
            AdvertisingConsent: false,
            Attribution: null));
        Assert.NotNull(created.User);

        // Повторная регистрация того же email падает на реальном Identity: имя пользователя занято.
        await using var second = CreateService(context);
        var rejected = await second.Service.CreateAsync(new AccountRegistrationRequest(
            email,
            Password,
            PersonalDataProcessingConsent: true,
            AdvertisingConsent: false,
            Attribution: null));

        Assert.Null(rejected.User);
        Assert.NotEmpty(rejected.Errors);
        second.Notifications.Verify(
            publisher => publisher.AddRegistrationNotificationsAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task TelegramOutboxWriteFailureRollsBackRegistrationAndSiteNotifications()
    {
        await using var context = fixture.CreateContext();
        await using var harness = CreateService(context);
        var email = $"registration-rollback-{Guid.NewGuid():N}@example.com";
        string? createdUserId = null;
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION accounts_reject_registration_outbox() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'Проверка отката регистрации при ошибке Telegram-outbox.';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER accounts_reject_registration_outbox
                BEFORE INSERT ON "TelegramNotificationOutbox"
                FOR EACH ROW EXECUTE FUNCTION accounts_reject_registration_outbox();
            """);

        try
        {
            harness.Notifications.Setup(item => item.AddRegistrationNotificationsAsync(It.IsAny<ApplicationUser>()))
                .Returns<ApplicationUser>(async user =>
                {
                    createdUserId = user.Id;
                    await harness.RegistrationPublisher.AddRegistrationNotificationsAsync(user);
                });

            await Assert.ThrowsAsync<DbUpdateException>(() => harness.Service.CreateAsync(
                new AccountRegistrationRequest(email, Password, true, true, null)));
            Assert.NotNull(createdUserId);
            Assert.Empty(context.ChangeTracker.Entries());
            await using var verification = fixture.CreateContext();
            Assert.False(await verification.Users.AnyAsync(item => item.Email == email));
            Assert.False(await verification.SiteNotifications.AnyAsync(item => item.RecipientUserId == createdUserId));
            Assert.False(await verification.TelegramNotificationOutbox.AnyAsync(item => item.NotificationKey == $"registration:{createdUserId}"));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER accounts_reject_registration_outbox ON "TelegramNotificationOutbox";
                DROP FUNCTION accounts_reject_registration_outbox();
                """);
        }
    }

    [Fact]
    public async Task RegistrationPersistsWelcomeAndPendingInvitationInTheSameTransaction()
    {
        await using var context = fixture.CreateContext();
        var email = $"registration-invited-{Guid.NewGuid():N}@example.com";
        var owner = new ApplicationUser { UserName = $"owner-{Guid.NewGuid():N}", Email = "owner@example.com" };
        var organization = new Organization { Name = "Приглашающая студия", OwnerUserId = owner.Id };
        var invitation = new OrganizationInvitation
        {
            Organization = organization,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            InvitedByUser = owner
        };

        context.Users.Add(owner);
        context.OrganizationInvitations.Add(invitation);
        await context.SaveChangesAsync();
        await using var harness = CreateService(context);

        var result = await harness.Service.CreateAsync(new AccountRegistrationRequest(
            email, Password, false, false, null));

        var user = Assert.IsType<ApplicationUser>(result.User);
        Assert.False(user.PersonalDataProcessingConsentAccepted);
        Assert.Null(user.PersonalDataProcessingConsentAcceptedAt);
        await using var verification = fixture.CreateContext();
        var keys = await verification.SiteNotifications.Where(item => item.RecipientUserId == user.Id)
            .Select(item => item.EventKey).ToListAsync();
        Assert.Contains($"welcome:{user.Id}", keys);
        Assert.Contains($"invitation:{invitation.Id}:{invitation.Token}", keys);
        Assert.True(await verification.TelegramNotificationOutbox.AnyAsync(item => item.NotificationKey == $"registration:{user.Id}"));
    }

    private static RegistrationHarness CreateService(OpenCRMContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication();
        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<OpenCRMContext>()
            .AddSignInManager();
        var notifications = new Mock<ISiteNotificationPublisher>();
        var notificationServices = new ServiceCollection();
        notificationServices.AddOpenCRMNotifications(new ConfigurationBuilder().Build());
        ((IServiceCollection)services).Add(notificationServices.Single(item => item.ServiceType == typeof(ISiteNotificationPublisher)));
        // Адаптер использует тот же контекст, что Identity и реальная транзакция регистрации.
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ISiteNotificationPublisher>();
        notifications.Setup(item => item.AddRegistrationNotificationsAsync(It.IsAny<ApplicationUser>()))
            .Returns<ApplicationUser>(publisher.AddRegistrationNotificationsAsync);
        var service = new AccountRegistrationService(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            new ApplicationUnitOfWork(context),
            notifications.Object,
            context);

        return new RegistrationHarness(provider, service, notifications, publisher);
    }

    private sealed class RegistrationHarness(
        ServiceProvider provider,
        AccountRegistrationService service,
        Mock<ISiteNotificationPublisher> notifications,
        ISiteNotificationPublisher registrationPublisher) : IAsyncDisposable
    {
        public AccountRegistrationService Service { get; } = service;

        public Mock<ISiteNotificationPublisher> Notifications { get; } = notifications;

        public ISiteNotificationPublisher RegistrationPublisher { get; } = registrationPublisher;

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
        }
    }
}
