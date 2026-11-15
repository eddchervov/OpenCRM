using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Media;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceCompositionTests
{
    [Fact]
    public void AllPublicPortsResolveInTheirOwnScopeWithSharedContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=127.0.0.1;Database=maintenance-composition;Username=test;Password=test")
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped(_ => new OpenCRMContext(options));
        services.AddSingleton(Mock.Of<IMaintenanceRepository>());
        services.AddSingleton(Mock.Of<IMaintenancePhotoRepository>());
        services.AddSingleton(Mock.Of<IVehicleMaintenanceLookupService>());
        services.AddSingleton(Mock.Of<IVehicleCounterCommandService>());
        services.AddSingleton(Mock.Of<IMaintenanceConsumptionService>());
        services.AddSingleton(Mock.Of<ISystemClock>());
        services.AddSingleton(Mock.Of<IOrganizationSubscriptionService>());
        services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
        services.AddSingleton(Mock.Of<IMaintenanceSchedulerSyncService>());
        services.AddSingleton(Mock.Of<IOrganizationInquiryOriginReader>());
        services.AddMemoryCache();
        services.AddSingleton(Mock.Of<IImageProcessingService>());
        services.AddSingleton(Mock.Of<IMediaWorkspaceAccess>());
        services.AddOpenCRMMaintenance();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var contracts = typeof(IMaintenanceQueryService).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .ToArray();

        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            var instance = first.ServiceProvider.GetRequiredService(contract);
            Assert.Same(instance, first.ServiceProvider.GetRequiredService(contract));
            Assert.NotSame(instance, second.ServiceProvider.GetRequiredService(contract));
            Assert.Equal(typeof(MaintenanceServiceCollectionExtensions).Assembly, instance.GetType().Assembly);
        }

        var context = first.ServiceProvider.GetRequiredService<OpenCRMContext>();
        Assert.Same(context, first.ServiceProvider.GetRequiredService<OpenCRMContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<OpenCRMContext>());
        Assert.Null(context.Database.CurrentTransaction);
    }
}
