using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure.Providers.SmsRu;
using Notifications.Infrastructure.Providers.Smtp;
using Notifications.Infrastructure.Templating;
using Razor.Templating.Core;

namespace Notifications.Infrastructure;

/// <summary>Регистрирует обработчики команд, доставку, хранилище и transactional outbox.</summary>
public static class DependencyInjection
{
    /// <summary>Подключает инфраструктуру единственного процесса Worker.</summary>
    public static IServiceCollection AddNotificationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool migrationsOnly = false)
    {
        var connectionString = configuration.GetConnectionString("NotificationsDb")
            ?? throw new InvalidOperationException("Connection string 'NotificationsDb' was not found.");

        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications")));
        services.AddOptions<TelegramChatOptions>()
            .Bind(configuration.GetSection(TelegramChatOptions.SectionName))
            .Validate(options => !options.Enabled ||
                IsValidHttpsBaseUrl(options.OpenCRMBaseUrl)
                && options.ServiceKey.Length >= 32
                && IsValidHttpsBaseUrl(options.RelayBaseUrl)
                && options.RelayKey.Length >= 32
                && options.RequestTimeoutSeconds is >= 5 and <= 120
                && options.PrefetchCount is > 0 and <= 64
                && options.ConcurrencyLimit is > 0 and <= 32
                && options.QueueMaxLength is >= 1 and <= 10000
                && options.QueueMaxBytes is >= 65536 and <= 67108864
                && options.InboxWorkerConcurrency is > 0 and <= 16
                && options.InboxPollIntervalSeconds is >= 1 and <= 60
                && options.InboxLeaseSeconds is >= 120 and <= 900,
                "TelegramChat requires HTTPS service URLs, long keys and bounded queue settings.")
            .ValidateOnStart();
        services.AddOptions<MaxChatOptions>()
            .Bind(configuration.GetSection(MaxChatOptions.SectionName))
            .Validate(options => !options.Enabled
                || IsValidHttpsBaseUrl(options.OpenCRMBaseUrl)
                    && options.ServiceKey.Length >= 32
                    && options.RequestTimeoutSeconds is >= 5 and <= 120,
                "MaxChat requires an HTTPS OpenCRM URL, a long service key and a bounded timeout.")
            .ValidateOnStart();
        if (migrationsOnly)
        {
            return services;
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MaxConversationRateLimiter>();
        services.AddScoped<IMaxConversationRateLimiter>(provider =>
            provider.GetRequiredService<MaxConversationRateLimiter>());
        services.AddScoped<NotificationApplicationService>();
        services.AddScoped<NotificationContentResolver>();
        services.AddScoped<TemplateApplicationService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NotificationDbContext>());
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationRepository>();
        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<IValidateOptions<SystemTemplateOptions>, SystemTemplateOptionsValidator>();
        services.AddOptions<SystemTemplateOptions>()
            .Bind(configuration.GetSection(SystemTemplateOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<ISystemTemplateRenderer, RazorSystemTemplateRenderer>();
        services.AddHostedService<ScheduledNotificationPublisher>();
        var telegramChatEnabled = configuration.GetValue<bool>($"{TelegramChatOptions.SectionName}:Enabled");
        var maxChatEnabled = configuration.GetValue<bool>($"{MaxChatOptions.SectionName}:Enabled");
        if (telegramChatEnabled || maxChatEnabled)
        {
            services.AddScoped<ConversationDeliveryAttemptStore>();
            services.AddScoped<ConversationDeliveryInboxStore>();
            var enabledProviders = new List<ConversationDeliveryProvider>();
            if (telegramChatEnabled)
            {
                services.AddScoped<IConversationDeliveryProcessor, TelegramChatDeliveryProcessor>();
                enabledProviders.Add(ConversationDeliveryProvider.Telegram);
            }

            if (maxChatEnabled)
            {
                services.AddScoped<IConversationDeliveryProcessor, MaxChatDeliveryProcessor>();
                enabledProviders.Add(ConversationDeliveryProvider.Max);
                var maxOptions = configuration.GetSection(MaxChatOptions.SectionName).Get<MaxChatOptions>()
                    ?? new MaxChatOptions();
                services.AddHttpClient("MaxChatOpenCRM", client =>
                {
                    client.BaseAddress = new Uri($"{maxOptions.OpenCRMBaseUrl.TrimEnd('/')}/");
                    client.Timeout = TimeSpan.FromSeconds(maxOptions.RequestTimeoutSeconds);
                }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
                services.AddHttpClient("MaxConversationApi", client =>
                {
                    client.BaseAddress = new Uri("https://platform-api2.max.ru/");
                    client.Timeout = TimeSpan.FromSeconds(maxOptions.RequestTimeoutSeconds);
                }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
                services.AddHttpClient("MaxConversationUpload", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(maxOptions.RequestTimeoutSeconds);
                }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            }

            services.AddSingleton<IReadOnlyCollection<ConversationDeliveryProvider>>(enabledProviders);
            services.AddHostedService<ConversationDeliveryInboxWorker>();
        }

        if (telegramChatEnabled)
        {
            services.AddHttpClient("TelegramChatOpenCRM", (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<TelegramChatOptions>>().Value;
                client.BaseAddress = new Uri($"{options.OpenCRMBaseUrl.TrimEnd('/')}/");
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            services.AddHttpClient("TelegramChatRelay", (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<TelegramChatOptions>>().Value;
                client.BaseAddress = new Uri($"{options.RelayBaseUrl.TrimEnd('/')}/");
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        }

        services.AddOptions<Providers.UnisenderGo.UnisenderGoOptions>()
            .Bind(configuration.GetSection("UnisenderGo"))
            .Validate(value => !value.WebhookEnabled || !string.IsNullOrWhiteSpace(value.WebhookKey),
                "Для приёма вебхуков требуется UnisenderGo:WebhookKey.")
            .ValidateOnStart();
        services.AddScoped<Providers.UnisenderGo.UnisenderGoWebhookHandler>();
        services.Configure<DeliveryOptions>(configuration.GetSection(DeliveryOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<SmsRuOptions>(configuration.GetSection(SmsRuOptions.SectionName));
        services.Configure<Providers.Telegram.TelegramOptions>("admin",
            configuration.GetSection(Providers.Telegram.TelegramOptions.SectionName));
        services.Configure<Providers.Telegram.TelegramOptions>("client",
            configuration.GetSection("TelegramClient"));
        services.Configure<Providers.Max.MaxOptions>(
            configuration.GetSection(Providers.Max.MaxOptions.SectionName));

        var applications = configuration.GetSection("RabbitMq:Applications").Get<string[]>()
            ?? throw new InvalidOperationException("RabbitMq:Applications must be set.");
        if (applications.Length == 0 || applications.Distinct(StringComparer.Ordinal).Count() != applications.Length)
        {
            throw new InvalidOperationException("RabbitMq:Applications must contain distinct application IDs.");
        }

        foreach (var applicationId in applications)
        {
            _ = NotificationQueues.Commands(applicationId);
        }

        string? conversationApplicationId = null;
        if (telegramChatEnabled || maxChatEnabled)
        {
            conversationApplicationId = configuration["Conversations:ApplicationId"]?.Trim();
            if (string.IsNullOrWhiteSpace(conversationApplicationId))
            {
                if (applications.Length != 1)
                {
                    throw new InvalidOperationException(
                        "Conversations:ApplicationId must be set when chat delivery is enabled for multiple applications.");
                }

                conversationApplicationId = applications[0];
            }

            if (!applications.Contains(conversationApplicationId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Conversations:ApplicationId must be listed in RabbitMq:Applications.");
            }

            var rootApplicationId = configuration["RabbitMq:ApplicationId"]?.Trim();
            if (!string.IsNullOrWhiteSpace(rootApplicationId)
                && !string.Equals(rootApplicationId, conversationApplicationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RabbitMq:ApplicationId must match Conversations:ApplicationId when chat delivery is enabled.");
            }
        }

        services.Configure<NotificationMessagingOptions>(options =>
        {
            options.Applications = applications;
            options.ConversationApplicationId = conversationApplicationId;
        });
        services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true);
        if (configuration.GetValue<bool>("Email:Enabled"))
        {
            StartupConfigurationValidator.RequireProvider(configuration, "Email", "smtp", "unisender-go");
            services.AddScoped<INotificationChannelSender, EmailNotificationSender>();
            if (configuration["Email:Provider"] == "unisender-go")
            {
                services.AddSingleton<Providers.Smtp.IEmailMessageEnricher, Providers.UnisenderGo.UnisenderGoMessageEnricher>();
            }
        }

        if (configuration.GetValue<bool>("Sms:Enabled"))
        {
            StartupConfigurationValidator.RequireProvider(configuration, "Sms", "smsru");
            services.AddHttpClient<INotificationChannelSender, SmsRuNotificationSender>((provider, client) =>
            {
                var options = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<SmsRuOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            });
        }

        if (configuration.GetValue<bool>("Telegram:Enabled"))
        {
            StartupConfigurationValidator.RequireProvider(configuration, "Telegram", "telegram");
            AddTelegramSender(services, "admin", "telegram-admin", NotificationChannel.Telegram);
        }

        if (configuration.GetValue<bool>("TelegramClient:Enabled"))
        {
            StartupConfigurationValidator.RequireProvider(configuration, "TelegramClient", "telegram");
            AddTelegramSender(services, "client", "telegram-client", NotificationChannel.TelegramClient);
        }

        if (configuration.GetValue<bool>("Max:Enabled"))
        {
            StartupConfigurationValidator.RequireProvider(configuration, "Max", "max");
            services.AddHttpClient<INotificationChannelSender, Providers.Max.MaxNotificationSender>(
                    (provider, client) =>
                    {
                        var options = provider.GetRequiredService<IOptions<Providers.Max.MaxOptions>>().Value;
                        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
                    })
                .RemoveAllLoggers();
        }

        var rabbitMqHost = StartupConfigurationValidator.RequireValue(configuration, "RabbitMq:Host");
        var rabbitMqPort = configuration.GetValue<ushort>("RabbitMq:Port");
        var rabbitMqVirtualHost = StartupConfigurationValidator.RequireValue(configuration, "RabbitMq:VirtualHost");
        var rabbitMqUsername = StartupConfigurationValidator.RequireValue(configuration, "RabbitMq:Username");
        var rabbitMqPassword = StartupConfigurationValidator.RequireValue(configuration, "RabbitMq:Password");

        services.AddMassTransit(configurator =>
        {
            configurator.AddEntityFrameworkOutbox<NotificationDbContext>(options =>
            {
                options.QueryDelay = TimeSpan.FromSeconds(1);
                options.UsePostgres();
                options.UseBusOutbox();
            });

            configurator.AddConsumer<NotificationDispatchConsumer>();
            configurator.AddConsumer<NotificationCommandConsumer>();
            telegramChatEnabled = configuration.GetValue<bool>($"{TelegramChatOptions.SectionName}:Enabled");
            maxChatEnabled = configuration.GetValue<bool>($"{MaxChatOptions.SectionName}:Enabled");
            if (telegramChatEnabled || maxChatEnabled)
            {
                configurator.AddConsumer<TelegramChatDeliveryConsumer>();
            }

            configurator.UsingRabbitMq((context, rabbitMq) =>
            {
                rabbitMq.Host(rabbitMqHost, rabbitMqPort, rabbitMqVirtualHost, host =>
                {
                    host.Username(rabbitMqUsername);
                    host.Password(rabbitMqPassword);
                });

                rabbitMq.SendTopology.ConfigureErrorSettings = settings =>
                {
                    settings.SetQueueArgument("x-max-length", 500);
                    settings.SetQueueArgument("x-max-length-bytes", 8 * 1024 * 1024);
                    settings.SetQueueArgument("x-overflow", "reject-publish");
                };

                foreach (var applicationId in applications)
                {
                    rabbitMq.ReceiveEndpoint(NotificationQueues.Commands(applicationId), endpoint =>
                    {
                        // Только адресная Send-команда: Publish не должен разослать её всем приложениям.
                        endpoint.ConfigureConsumeTopology = false;
                        endpoint.PublishFaults = false;
                        endpoint.PrefetchCount = 16;
                        endpoint.UseMessageRetry(retry => retry.Intervals(
                            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                        endpoint.UseEntityFrameworkOutbox<NotificationDbContext>(context);
                        endpoint.ConfigureConsumer<NotificationCommandConsumer>(context);
                    });

                    if (telegramChatEnabled && applicationId == conversationApplicationId)
                    {
                        var chatOptions = configuration.GetSection(TelegramChatOptions.SectionName)
                            .Get<TelegramChatOptions>() ?? new TelegramChatOptions();
                        ConfigureConversationDeliveryEndpoint(
                            rabbitMq,
                            NotificationQueues.TelegramChat(applicationId),
                            chatOptions,
                            context);
                        ConfigureConversationDeliveryEndpoint(
                            rabbitMq,
                            NotificationQueues.ConversationDelivery(
                                applicationId,
                                ConversationDeliveryProvider.Telegram),
                            chatOptions,
                            context);
                    }

                    if (maxChatEnabled && applicationId == conversationApplicationId)
                    {
                        var chatOptions = configuration.GetSection(TelegramChatOptions.SectionName)
                            .Get<TelegramChatOptions>() ?? new TelegramChatOptions();
                        ConfigureConversationDeliveryEndpoint(
                            rabbitMq,
                            NotificationQueues.ConversationDelivery(applicationId, ConversationDeliveryProvider.Max),
                            chatOptions,
                            context);
                    }
                }

                // Имя и контракт сохраняются для заданий, принятых предыдущей версией сервиса.
                rabbitMq.ReceiveEndpoint("notifications-dispatch", endpoint =>
                {
                    endpoint.PublishFaults = false;
                    endpoint.PrefetchCount = 16;
                    endpoint.UseMessageRetry(retry => retry.Intervals(
                        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));
                    endpoint.ConfigureConsumer<NotificationDispatchConsumer>(context);
                });
            });
        });

        services.AddHealthChecks().AddCheck<NotificationDatabaseHealthCheck>(
            "notifications-database", timeout: TimeSpan.FromSeconds(3));
        services.AddRazorTemplating();

        return services;
    }

    private static void ConfigureConversationDeliveryEndpoint(
        IRabbitMqBusFactoryConfigurator rabbitMq,
        string queueName,
        TelegramChatOptions chatOptions,
        IRegistrationContext context)
    {
        rabbitMq.ReceiveEndpoint(queueName, endpoint =>
        {
            endpoint.ConfigureConsumeTopology = false;
            endpoint.PublishFaults = false;
            endpoint.PrefetchCount = chatOptions.PrefetchCount;
            endpoint.ConcurrentMessageLimit = chatOptions.ConcurrencyLimit;
            endpoint.SetQueueArgument("x-max-length", chatOptions.QueueMaxLength);
            endpoint.SetQueueArgument("x-max-length-bytes", chatOptions.QueueMaxBytes);
            endpoint.SetQueueArgument("x-overflow", "reject-publish");
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));
            endpoint.ConfigureConsumer<TelegramChatDeliveryConsumer>(context);
        });

    }

    /// <summary>Применяет миграции в отдельной области зависимостей.</summary>
    public static async Task ApplyNotificationMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static bool IsValidHttpsBaseUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    /// <summary> Регистрирует отправитель сообщений через Telegram. </summary>
    private static void AddTelegramSender(
        IServiceCollection services,
        string optionsName,
        string clientName,
        NotificationChannel channel)
    {
        services.AddHttpClient(clientName, (provider, client) =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org");
            var options = provider.GetRequiredService<IOptionsMonitor<Providers.Telegram.TelegramOptions>>()
                .Get(optionsName);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        }).RemoveAllLoggers();
        services.AddTransient<INotificationChannelSender>(provider => new Providers.Telegram.TelegramNotificationSender(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName),
            provider.GetRequiredService<IOptions<DeliveryOptions>>().Value,
            provider.GetRequiredService<IOptionsMonitor<Providers.Telegram.TelegramOptions>>().Get(optionsName),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Providers.Telegram.TelegramNotificationSender>>(),
            channel));
    }
}
