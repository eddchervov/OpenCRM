using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Service.Messaging;
using Payments.Service.Persistence;
using Payments.Service.Processing;
using Payments.Service.Providers;

namespace Payments.Service.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddPayments(this IServiceCollection services, IConfiguration configuration,
        bool migrationsOnly = false)
    {
        var connectionString = configuration.GetConnectionString("PaymentsDb")
            ?? throw new InvalidOperationException("Задайте отдельную строку подключения PaymentsDb.");

        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments")));
        if (migrationsOnly)
        {
            return services;
        }

        services.AddOptions<PaymentOptions>().Bind(configuration.GetSection("Payments"))
            .Validate(ServiceRegistration.IsValid, "Некорректная конфигурация Payments; боевой режим не поддерживается.")
            .ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host) && options.Port > 0
                && !string.IsNullOrWhiteSpace(options.VirtualHost) && options.VirtualHost != "/"
                && !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password),
                "Задайте отдельный RabbitMQ virtual host и учётные данные Payments.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<PaymentCommandService>();
        services.AddScoped<PaymentEventPublisher>();
        services.AddScoped<PaymentProcessor>();
        services.AddHostedService<PaymentWorker>();
        if (configuration["Payments:Mode"] is "YooKassaTest" or "YooKassaLive")
        {
            services.AddHttpClient<IPaymentProvider, YooKassaPaymentProvider>(client =>
            {
                client.BaseAddress = new Uri("https://api.yookassa.ru/v3/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                .RemoveAllLoggers();
        }
        else
        {
            services.AddScoped<IPaymentProvider, DisabledPaymentProvider>();
        }

        var rabbit = configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        var applicationId = configuration["Payments:ApplicationId"] ?? "opencrm";
        services.AddMassTransit(registration =>
        {
            registration.AddEntityFrameworkOutbox<PaymentDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });
            registration.AddConsumer<PaymentCommandConsumer>();
            registration.UsingRabbitMq((context, bus) =>
            {
                bus.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                });

                bus.ReceiveEndpoint($"payments-{applicationId}-commands", endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.PublishFaults = false;
                    endpoint.PrefetchCount = 8;
                    endpoint.ConcurrentMessageLimit = 4;
                    endpoint.UseMessageRetry(retry => retry.Intervals(
                        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
                    endpoint.UseEntityFrameworkOutbox<PaymentDbContext>(context);
                    endpoint.ConfigureConsumer<PaymentCommandConsumer>(context);
                });
            });
        });

        services.AddHealthChecks().AddCheck<PaymentDatabaseHealthCheck>("payments-database");

        return services;
    }

    public static bool IsValid(PaymentOptions options)
    {
        if (options.Mode is not ("Disabled" or "YooKassaTest" or "YooKassaLive") || options.ApplicationId != "opencrm"
            || string.IsNullOrWhiteSpace(options.ServiceKey) || options.ServiceKey.Length < 32 || options.PollSeconds is < 1 or > 60
            || options.RetryWindowHours is < 1 or > 23 || options.MerchantAccountId.Length is < 1 or > 128
            || options.AllowedReturnUrls.Length == 0
            || options.AllowedReturnUrls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || url.Length > 2048))
        {
            return false;
        }

        if (options.Mode == "Disabled")
        {
            return true;
        }

        var isTestMode = options.Mode == "YooKassaTest";

        return options.MerchantAccountId.All(char.IsAsciiDigit)
            && options.IsTest == isTestMode
            && options.SecretKey.StartsWith(isTestMode ? "test_" : "live_", StringComparison.Ordinal);
    }
}
