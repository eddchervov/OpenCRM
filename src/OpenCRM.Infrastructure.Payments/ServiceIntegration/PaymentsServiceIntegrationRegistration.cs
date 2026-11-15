using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenCRM.Modules.Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Регистрирует отдельный финансовый транспорт без доступа к БД платёжного сервиса. </summary>
internal static class PaymentsServiceIntegrationRegistration
{
    public static void AddPaymentsServiceIntegration(IServiceCollection services, IConfiguration configuration)
    {
        var commercialEnabled = configuration.GetValue("PlatformPolicy:Commercial:Enabled", false);
        services.AddOptions<PaymentsServiceOptions>().Bind(configuration.GetSection("PaymentsService"))
            .PostConfigure(options =>
            {
                if (!commercialEnabled)
                {
                    options.Enabled = false;
                }
            })
            .Validate(PaymentsServiceOptions.IsValid, "Некорректное или небезопасное подключение к платёжному сервису.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        if (commercialEnabled)
        {
            services.AddHostedService<PaymentsServiceRecoveryGuard>();
        }
        services.AddScoped<MicroservicePaymentCreationGateway>();
        services.AddScoped<IPaymentPurchaseQueryService>(provider => provider.GetRequiredService<MicroservicePaymentCreationGateway>());
        services.AddScoped<YooKassaPaymentCreationGateway>();
        services.AddScoped<IPaymentCreationGateway, RoutedPaymentCreationGateway>();
        services.AddScoped<SubscriptionPaymentEventProcessor>();
        services.AddScoped<SubscriptionPurchaseApplicator>();
        services.AddScoped<SubscriptionPaymentReconciler>();
        services.AddScoped<IAdminPaymentServiceActions, SubscriptionPaymentAdminService>();
        services.AddScoped<SubscriptionPaymentAdminWorker>();
        services.AddHttpClient("OpenCRM.PaymentsService", (provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<PaymentsServiceOptions>>().Value;
            if (settings.Enabled)
            {
                client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Add("X-Payments-Key", settings.ServiceKey);
            }

            client.Timeout = TimeSpan.FromSeconds(15);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
        if (!commercialEnabled || !configuration.GetValue<bool>("PaymentsService:Enabled"))
        {
            return;
        }

        services.AddScoped<SubscriptionPaymentOutboxDispatcher>();
        services.AddScoped<ISubscriptionPaymentCommandTransport, PaymentsServiceCommandTransport>();
        var settings = configuration.GetSection("PaymentsService").Get<PaymentsServiceOptions>() ?? new();
        services.AddMassTransit<IPaymentsServiceBus>(registration =>
        {
            registration.AddConsumer<PaymentsServiceResultConsumer>();
            registration.UsingRabbitMq((context, transport) =>
            {
                transport.Host(settings.Host, settings.Port, settings.VirtualHost, host =>
                {
                    host.Username(settings.Username);
                    host.Password(settings.Password);
                });
                transport.ReceiveEndpoint("payments-opencrm-events", endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.PublishFaults = false;
                    endpoint.PrefetchCount = 8;
                    endpoint.ConcurrentMessageLimit = 4;
                    endpoint.UseMessageRetry(retry => retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                    endpoint.ConfigureConsumer<PaymentsServiceResultConsumer>(context);
                });
            });
        });
        services.AddHostedService<SubscriptionPaymentWorker>();
    }
}
