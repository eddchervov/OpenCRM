using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.Modules.Billing.Tests.Integration;

internal static class BillingExternalDependencies
{
    internal static void AddBillingExternalDependencies(this IServiceCollection services)
    {
        services.AddSingleton(Mock.Of<IVehicleQueryService>());
        services.AddSingleton(Mock.Of<IPromotionPaymentCoordinator>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Mock.Of<IEmailNotificationService>());
        services.AddSingleton(Mock.Of<ISiteNotificationPublisher>());
    }
}
