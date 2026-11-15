using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Методы расширения регистрации инфраструктуры платежей. </summary>
public static class PaymentsInfrastructureServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы платёжной инфраструктуры в контейнере. </summary>
    public static IServiceCollection AddOpenCRMPaymentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IYooKassaService, YooKassaService>();
        services.AddScoped<IPaymentCompletionService, PaymentCompletionService>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<INpdReceiptDeliveryService, NpdReceiptDeliveryService>();
        services.AddHttpClient("OpenCRM.PaymentCreation", client => ConfigureYooKassaHttpClient(client));
        services.AddScoped<PaymentCreationProcessor>(provider => new PaymentCreationProcessor(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenCRM.PaymentCreation"),
            provider.GetRequiredService<OpenCRMContext>(),
            provider.GetRequiredService<ILogger<PaymentCreationProcessor>>(),
            provider.GetRequiredService<IOptions<YooKassaConfig>>().Value,
            provider.GetRequiredService<IPromotionPaymentCoordinator>()));
        PaymentsServiceIntegrationRegistration.AddPaymentsServiceIntegration(services, configuration);
        services.AddScoped<IBillingAutomationService>(provider => new BillingAutomationService(
            provider.GetRequiredService<OpenCRMContext>(),
            provider.GetRequiredService<IYooKassaService>(),
            provider.GetRequiredService<IPaymentConfirmationService>(),
            provider.GetRequiredService<ILogger<BillingAutomationService>>(),
            provider.GetRequiredService<PaymentCreationProcessor>()));
        services.AddScoped<IYooKassaWebhookService, YooKassaWebhookService>();

        services.Configure<YooKassaConfig>(configuration.GetSection("YooKassa"));

        services.AddHttpClient<IYooKassaService, YooKassaService>(client => ConfigureYooKassaHttpClient(client));

        return services;
    }

    /// <summary> Настраивает HTTP-клиент YooKassa. </summary>
    private static void ConfigureYooKassaHttpClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.yookassa.ru/v3/");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    }
}
