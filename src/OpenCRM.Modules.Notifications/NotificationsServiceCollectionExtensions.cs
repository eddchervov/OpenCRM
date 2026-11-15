using MassTransit;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCRM.Modules.Notifications.Sms;
using OpenCRM.Services.Interfaces.Sms;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Modules.Notifications.Telegram;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary> Методы регистрации сервисов модуля уведомлений. </summary>
public static class NotificationsServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы уведомлений и инфраструктуру RabbitMQ. </summary>
    public static IServiceCollection AddOpenCRMNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransient<ISmsSendingHistoryService, SmsSendingHistoryService>();
        services.AddHttpClient("MaxBot", client =>
        {
            client.BaseAddress = new Uri("https://platform-api2.max.ru/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<OrganizationMaxService>();
        services.AddScoped<IOrganizationChannelCredentialReader, OrganizationChannelCredentialReader>();
        services.AddScoped<IOrganizationMaxFileReader, OrganizationMaxFileReader>();
        services.AddScoped<IOrganizationChannelFileReader, OrganizationChannelFileReader>();
        services.AddHttpClient("MaxConversationFile", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(MaxCdnHttpHandlerFactory.Create);
        if (configuration.GetValue<bool>("MaxNotifications:Enabled"))
        {
            if (configuration["MaxNotifications:ServiceKey"] is not { Length: >= 32 })
            {
                throw new InvalidOperationException("Для уведомлений MAX нужен MaxNotifications:ServiceKey длиной не менее 32 символов.");
            }

            services.AddHostedService<MaxNotificationWorker>();
        }

        services.AddHttpClient("TelegramOrganizationBot", client =>
        {
            var relayBaseUrl = configuration["TelegramOrganizationNotifications:RelayBaseUrl"];
            if (Uri.TryCreate(relayBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                client.BaseAddress = new Uri($"{relayBaseUrl.TrimEnd('/')}/");
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<OrganizationTelegramService>();
        services.AddScoped<IOrganizationTelegramCredentialReader, OrganizationTelegramCredentialReader>();
        services.AddScoped<IOrganizationTelegramFileReader, OrganizationTelegramFileReader>();
        services.AddHttpClient("TelegramOrganizationFileRelay", client =>
        {
            var relayBaseUrl = configuration["TelegramOrganizationNotifications:RelayBaseUrl"];
            if (Uri.TryCreate(relayBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                client.BaseAddress = new Uri($"{relayBaseUrl.TrimEnd('/')}/");
            }

            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        if (configuration.GetValue<bool>("TelegramChat:Enabled")
            || configuration.GetValue<bool>("MaxChat:Enabled"))
        {
            services.AddScoped<ConversationDispatchDispatcher>();
            services.AddHostedService<ConversationDispatchWorker>();
        }
        if (configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled"))
        {
            if (configuration["TelegramOrganizationNotifications:ServiceKey"] is not { Length: >= 32 }
                || configuration["TelegramOrganizationNotifications:RelayKey"] is not { Length: >= 32 }
                || !Uri.TryCreate(configuration["TelegramOrganizationNotifications:RelayBaseUrl"], UriKind.Absolute, out var relayUri)
                || relayUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(relayUri.UserInfo)
                || !string.IsNullOrEmpty(relayUri.Query)
                || !string.IsNullOrEmpty(relayUri.Fragment))
            {
                throw new InvalidOperationException("Для клиентского Telegram нужны ServiceKey, RelayKey и HTTPS RelayBaseUrl.");
            }

            services.AddHostedService<TelegramClientNotificationWorker>();
        }

        services.AddTransient<IEmailSender, EmailSender>();
        services.AddScoped<IEmailCampaignService, EmailCampaignService>();
        services.AddScoped<IAdminEmailJournalService, AdminEmailJournalService>();
        services.AddScoped<IAdminEmailCompositionService, AdminEmailCompositionService>();
        services.AddScoped<IAdminEmailTestOperationsService, AdminEmailTestOperationsService>();
        services.AddScoped<IEmailUnsubscribeService, EmailUnsubscribeService>();
        services.AddScoped<ISiteNotificationPublisher, SiteNotificationPublisherAdapter>();
        services.AddScoped<ISiteNotificationService, SiteNotificationService>();
        services.AddScoped<IAdminSiteNotificationService, AdminSiteNotificationService>();

        // Встроенный транспорт выбирается явно; неполные реквизиты брокера не меняют режим доставки.
        var transportMode = configuration["Notifications:Transport"]
            ?? (configuration.GetValue<bool>("LocalTesting:UseInMemoryNotifications") ? "InMemory" : "RabbitMq");
        if (!string.Equals(transportMode, "Disabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transportMode, "InMemory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(transportMode, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Notifications:Transport должен быть Disabled, InMemory или RabbitMq.");
        }

        var notificationsDisabled = string.Equals(transportMode, "Disabled", StringComparison.OrdinalIgnoreCase);
        var useInMemoryNotifications = notificationsDisabled || string.Equals(transportMode, "InMemory", StringComparison.OrdinalIgnoreCase);

        var rabbitMqOptionsBuilder = services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection("RabbitMq"))
            .Validate(x => x.DispatchIntervalSeconds > 0 && x.ResponseTimeoutSeconds > 0
                && x.SendTimeoutSeconds > 0, "Интервалы RabbitMq должны быть положительными.");
        if (!useInMemoryNotifications)
        {
            // Строгая проверка подключения нужна только при работе через внешний брокер.
            rabbitMqOptionsBuilder
                .Validate(x => !string.IsNullOrWhiteSpace(x.Host) && x.Port > 0
                    && !string.IsNullOrWhiteSpace(x.VirtualHost)
                    && !string.IsNullOrWhiteSpace(x.Username)
                    && !string.IsNullOrWhiteSpace(x.Password), "Не заполнено подключение RabbitMq.")
                .ValidateOnStart();
        }

        services.AddScoped<IEmailNotificationService, EmailSender>();
        services.AddScoped<NotificationOutboxDispatcher>();
        if (!useInMemoryNotifications)
        {
            services.AddHostedService<NotificationOutboxWorker>();
        }
        if (configuration.GetValue<bool>("TelegramAdminNotifications:Enabled"))
        {
            services.AddScoped<TelegramNotificationOutboxDispatcher>();
            services.AddHostedService<TelegramNotificationOutboxWorker>();
        }

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<NotificationResultConsumer>();
            bus.AddConsumer<NotificationDeliveryConsumer>();

            if (useInMemoryNotifications)
            {
                var applicationId = configuration["RabbitMq:ApplicationId"] ?? "opencrm-local";

                bus.UsingInMemory((context, transport) =>
                {
                    transport.ReceiveEndpoint(NotificationQueues.Results(applicationId), endpoint =>
                    {
                        endpoint.ConfigureConsumeTopology = false;
                        endpoint.ConfigureConsumer<NotificationResultConsumer>(context);
                        endpoint.ConfigureConsumer<NotificationDeliveryConsumer>(context);
                    });
                });

                return;
            }

            bus.UsingRabbitMq((context, transport) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                transport.Host(options.Host, options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                });
                transport.ReceiveEndpoint(NotificationQueues.Results(options.ApplicationId), endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.ConcurrentMessageLimit = 1;
                    endpoint.UseMessageRetry(retry => retry.Intervals(
                        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                    endpoint.ConfigureConsumer<NotificationResultConsumer>(context);
                    endpoint.ConfigureConsumer<NotificationDeliveryConsumer>(context);
                });
            });
        });

        return services;
    }

}
