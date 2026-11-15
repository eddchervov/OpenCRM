using MassTransit;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Modules.Notifications.Telegram;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class NotificationsModuleRegistrationTests
{
    [Fact]
    public async Task DisabledDeliveryDoesNotRunEmailOutboxWorker()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Notifications:Transport"] = "Disabled"
        }));

        var workers = await NotificationsModuleRegistrationTests.GetHostedServiceTypesAsync(services);

        Assert.DoesNotContain(typeof(NotificationOutboxWorker), workers);
    }

    [Fact]
    public void UnknownTransportIsRejectedInsteadOfSilentlyFallingBack()
    {
        var services = this.CreateServices();
        var configuration = this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["Notifications:Transport"] = "Typo"
        });

        Assert.Throws<InvalidOperationException>(() => services.AddOpenCRMNotifications(configuration));
    }

    [Fact]
    public void RegistersEmailAndSiteServicesWithTheirExistingLifetimesFromTheModuleAssembly()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration());

        AssertLifetime<IEmailSender>(services, ServiceLifetime.Transient, typeof(EmailSender));
        AssertLifetime<IEmailNotificationService>(services, ServiceLifetime.Scoped, typeof(EmailSender));
        AssertLifetime<IEmailCampaignService>(services, ServiceLifetime.Scoped, typeof(EmailCampaignService));
        AssertLifetime<IAdminEmailJournalService>(services, ServiceLifetime.Scoped, typeof(AdminEmailJournalService));
        AssertLifetime<IAdminEmailCompositionService>(services, ServiceLifetime.Scoped, typeof(AdminEmailCompositionService));
        AssertLifetime<IAdminEmailTestOperationsService>(services, ServiceLifetime.Scoped, typeof(AdminEmailTestOperationsService));
        AssertLifetime<IEmailUnsubscribeService>(services, ServiceLifetime.Scoped, typeof(EmailUnsubscribeService));
        AssertLifetime<ISiteNotificationPublisher>(services, ServiceLifetime.Scoped);
        AssertLifetime<ISiteNotificationService>(services, ServiceLifetime.Scoped, typeof(SiteNotificationService));
        AssertLifetime<IAdminSiteNotificationService>(services, ServiceLifetime.Scoped, typeof(AdminSiteNotificationService));

        Assert.All(
            services.Where(service => service.ImplementationType is not null
                && service.ImplementationType.Namespace?.StartsWith("OpenCRM.Modules.Notifications", StringComparison.Ordinal) == true),
            service => Assert.Same(typeof(EmailSender).Assembly, service.ImplementationType!.Assembly));
    }

    [Fact]
    public async Task DisabledClientNotificationWorkersAreNotRegistered()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration());
        var workerTypes = await NotificationsModuleRegistrationTests.GetHostedServiceTypesAsync(services);

        Assert.DoesNotContain(typeof(MaxNotificationWorker), workerTypes);
        Assert.DoesNotContain(typeof(TelegramClientNotificationWorker), workerTypes);
        Assert.Contains(typeof(NotificationOutboxWorker), workerTypes);
        Assert.DoesNotContain(typeof(TelegramNotificationOutboxWorker), workerTypes);
    }

    [Fact]
    public async Task TelegramAdminOutboxWorkerIsRegisteredOnlyWhenEnabled()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["TelegramAdminNotifications:Enabled"] = "true"
        }));

        Assert.Contains(typeof(TelegramNotificationOutboxWorker), await NotificationsModuleRegistrationTests.GetHostedServiceTypesAsync(services));
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, service =>
            service.ServiceType == typeof(TelegramNotificationOutboxDispatcher)).Lifetime);
    }

    [Fact]
    public async Task EnabledClientNotificationWorkersAreRegisteredWhenKeysAndRelayAreValid()
    {
        var services = this.CreateServices();
        var configuration = this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["MaxNotifications:Enabled"] = "true",
            ["MaxNotifications:ServiceKey"] = new string('m', 32),
            ["TelegramOrganizationNotifications:Enabled"] = "true",
            ["TelegramOrganizationNotifications:ServiceKey"] = new string('s', 32),
            ["TelegramOrganizationNotifications:RelayKey"] = new string('r', 32),
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://relay.example.com/api/"
        });

        services.AddOpenCRMNotifications(configuration);

        var workerTypes = await NotificationsModuleRegistrationTests.GetHostedServiceTypesAsync(services);

        Assert.Contains(typeof(MaxNotificationWorker), workerTypes);
        Assert.Contains(typeof(TelegramClientNotificationWorker), workerTypes);
    }

    [Fact]
    public async Task EnabledClientWorkersResolveWithRealMassTransitRegistrationAndScopeValidation()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["MaxNotifications:Enabled"] = "true",
            ["MaxNotifications:ServiceKey"] = new string('m', 32),
            ["TelegramOrganizationNotifications:Enabled"] = "true",
            ["TelegramOrganizationNotifications:ServiceKey"] = new string('s', 32),
            ["TelegramOrganizationNotifications:RelayKey"] = new string('r', 32),
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://relay.example.com/api/"
        }));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var hostedServices = provider.GetServices<IHostedService>().ToArray();

        Assert.Contains(hostedServices, service => service is MaxNotificationWorker);
        Assert.Contains(hostedServices, service => service is TelegramClientNotificationWorker);
    }

    [Theory]
    [InlineData("MaxNotifications:ServiceKey", "short", "https://relay.example.com/")]
    [InlineData("MaxNotifications:ServiceKey", null, "https://relay.example.com/")]
    [InlineData("TelegramOrganizationNotifications:ServiceKey", "short", "https://relay.example.com/")]
    [InlineData("TelegramOrganizationNotifications:RelayKey", "short", "https://relay.example.com/")]
    [InlineData("TelegramOrganizationNotifications:RelayBaseUrl", "https://relay.example.com/", "http://relay.example.com/")]
    [InlineData("TelegramOrganizationNotifications:RelayBaseUrl", "https://relay.example.com/", "not-a-url")]
    public void EnabledClientNotificationsRejectInvalidKeysOrRelayUrl(
        string invalidSetting,
        string? value,
        string relayUrl)
    {
        var values = new Dictionary<string, string?>
        {
            ["MaxNotifications:Enabled"] = "true",
            ["MaxNotifications:ServiceKey"] = new string('m', 32),
            ["TelegramOrganizationNotifications:Enabled"] = "true",
            ["TelegramOrganizationNotifications:ServiceKey"] = new string('s', 32),
            ["TelegramOrganizationNotifications:RelayKey"] = new string('r', 32),
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://relay.example.com/"
        };

        if (invalidSetting == "MaxNotifications:ServiceKey")
        {
            values[invalidSetting] = value;
        }
        else if (invalidSetting == "TelegramOrganizationNotifications:RelayBaseUrl")
        {
            values[invalidSetting] = relayUrl;
        }
        else
        {
            values[invalidSetting] = value;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            this.CreateServices().AddOpenCRMNotifications(this.CreateConfiguration(values)));

        Assert.Contains(
            invalidSetting.StartsWith("Max", StringComparison.Ordinal)
                ? "Для уведомлений MAX"
                : "Для клиентского Telegram",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationBotHttpClientsKeepTheirConfiguredAddressesAndTimeouts()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://relay.example.com/api///"
        }));
        await using var provider = services.BuildServiceProvider();
        var clientFactory = provider.GetRequiredService<IHttpClientFactory>();
        using var maxClient = clientFactory.CreateClient("MaxBot");
        using var telegramClient = clientFactory.CreateClient("TelegramOrganizationBot");

        Assert.Equal(new Uri("https://platform-api2.max.ru/"), maxClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(15), maxClient.Timeout);
        Assert.Equal(new Uri("https://relay.example.com/api/"), telegramClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(15), telegramClient.Timeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/relative")]
    [InlineData("http://relay.example.com")]
    public async Task DisabledTelegramClientDoesNotUseAnInvalidOrInsecureRelayAddress(string? address)
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(new Dictionary<string, string?>
        {
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = address
        }));
        await using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("TelegramOrganizationBot");

        Assert.Null(client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
    }

    [Fact]
    public async Task RabbitOptionsAreBoundAndValidated()
    {
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Equal("rabbit", options.Host);
        Assert.Equal((ushort)5672, options.Port);
        Assert.Equal("opencrm", options.VirtualHost);
        Assert.Equal("app", options.ApplicationId);
        Assert.Equal(5, options.DispatchIntervalSeconds);
        Assert.Equal(30, options.ResponseTimeoutSeconds);
        Assert.Equal(30, options.SendTimeoutSeconds);
    }

    [Theory]
    [InlineData("RabbitMq:Host", "")]
    [InlineData("RabbitMq:Port", "0")]
    [InlineData("RabbitMq:VirtualHost", "")]
    [InlineData("RabbitMq:Username", "")]
    [InlineData("RabbitMq:Password", "")]
    [InlineData("RabbitMq:DispatchIntervalSeconds", "0")]
    [InlineData("RabbitMq:ResponseTimeoutSeconds", "0")]
    [InlineData("RabbitMq:SendTimeoutSeconds", "0")]
    public async Task RabbitOptionsRejectMissingConnectionSettingsAndNonpositiveIntervals(string key, string value)
    {
        var values = this.DefaultConfigurationValues();
        values[key] = value;
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(values));
        await using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BusControlCanBeResolvedForConfiguredTransportWithoutStartingIt(bool useInMemory)
    {
        var values = this.DefaultConfigurationValues();
        values["LocalTesting:UseInMemoryNotifications"] = useInMemory.ToString();
        var services = this.CreateServices();
        services.AddOpenCRMNotifications(this.CreateConfiguration(values));
        await using var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<IBusControl>();

        Assert.NotNull(bus);
        Assert.Same(bus, provider.GetRequiredService<IBus>());
    }

    [Fact]
    public async Task ScopedPublisherCanBeResolvedWithoutConnectingToDatabase()
    {
        var services = this.CreateServices();
        services.AddDbContext<OpenCRMContext>(options => options.UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=notifications-di;Username=test;Password=test;Timeout=1;Command Timeout=1"));
        services.AddOpenCRMNotifications(this.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var publisher = scope.ServiceProvider.GetRequiredService<ISiteNotificationPublisher>();

        Assert.NotNull(publisher);
    }

    private static void AssertLifetime<TService>(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Type? implementationType = null)
    {
        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(TService));
        Assert.Equal(lifetime, descriptor.Lifetime);
        if (implementationType is not null)
        {
            Assert.Equal(implementationType, descriptor.ImplementationType);
        }

        if (descriptor.ImplementationType is not null)
        {
            Assert.Equal(typeof(EmailSender).Assembly, descriptor.ImplementationType.Assembly);
        }
    }

    private static async Task<Type[]> GetHostedServiceTypesAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();

        return provider.GetServices<IHostedService>().Select(service => service.GetType()).ToArray();
    }

    private IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(this.CreateConfiguration());
        services.AddSingleton<IEmailCampaignScheduler>(new NoOpEmailCampaignScheduler());

        return services;
    }

    private IConfiguration CreateConfiguration(Dictionary<string, string?>? overrides = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(this.DefaultConfigurationValues(overrides))
            .Build();
    }

    private Dictionary<string, string?> DefaultConfigurationValues(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbit",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:VirtualHost"] = "opencrm",
            ["RabbitMq:Username"] = "user",
            ["RabbitMq:Password"] = "password",
            ["RabbitMq:ApplicationId"] = "app",
            ["RabbitMq:DispatchIntervalSeconds"] = "5",
            ["RabbitMq:ResponseTimeoutSeconds"] = "30",
            ["RabbitMq:SendTimeoutSeconds"] = "30"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private sealed class NoOpEmailCampaignScheduler : IEmailCampaignScheduler
    {
        public void ScheduleNext(Guid campaignId, TimeSpan delay)
        {
        }
    }
}
