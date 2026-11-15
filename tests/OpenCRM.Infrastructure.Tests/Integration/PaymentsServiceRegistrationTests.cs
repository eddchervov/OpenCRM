using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentsServiceRegistrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DisabledIntegrationValidatesAndResolvesCheckoutWithoutFinancialBus(bool commercialEnabled, bool integrationRequested)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OpenCRMContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=test;Password=test"));
        services.AddSingleton(Mock.Of<IPromotionPaymentCoordinator>());
        services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
        services.Configure<YooKassaConfig>(_ => { });
        using var client = new HttpClient();
        services.AddScoped(provider => new PaymentCreationProcessor(client,
            provider.GetRequiredService<OpenCRMContext>(), NullLogger<PaymentCreationProcessor>.Instance,
            new YooKassaConfig(), provider.GetRequiredService<IPromotionPaymentCoordinator>()));
        PaymentsServiceIntegrationRegistration.AddPaymentsServiceIntegration(services, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = commercialEnabled.ToString(),
            ["PaymentsService:Enabled"] = integrationRequested.ToString()
        }).Build());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPaymentCreationGateway>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPaymentPurchaseQueryService>());
        Assert.Null(scope.ServiceProvider.GetService<IPaymentsServiceBus>());
        Assert.Null(scope.ServiceProvider.GetService<ISubscriptionPaymentCommandTransport>());
        Assert.False(provider.GetRequiredService<IOptions<PaymentsServiceOptions>>().Value.Enabled);
        Assert.Equal(commercialEnabled, services.Any(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && descriptor.ImplementationType == typeof(PaymentsServiceRecoveryGuard)));
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && descriptor.ImplementationType == typeof(SubscriptionPaymentWorker));
    }

}
