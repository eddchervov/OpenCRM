using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class InventoryModuleRegistrationTests
{
    [Fact]
    public void ModuleOwnsAllFiveScopedContractsAndDoesNotDependOnBlOrVehicles()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options));
        services.AddSingleton(Mock.Of<IOrganizationSubscriptionService>());
        services.AddScoped(_ => Mock.Of<ICatalogMaterialLookup>());
        services.AddOpenCRMInventory();
        var contracts = new[]
        {
            typeof(IInventoryQueryService),
            typeof(IOperationMaterialQueryService),
            typeof(IStockCommandService),
            typeof(IOperationMaterialCommandService),
            typeof(IMaintenanceConsumptionService)
        };
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        foreach (var contract in contracts)
        {
            var descriptor = Assert.Single(services, item => item.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            var first = firstScope.ServiceProvider.GetRequiredService(contract);
            Assert.Equal("OpenCRM.Modules.Inventory", first.GetType().Assembly.GetName().Name);
            Assert.Same(first, firstScope.ServiceProvider.GetRequiredService(contract));
            Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService(contract));
        }

        var dependencies = typeof(InventoryServiceCollectionExtensions).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(dependencies, item => item.Name == "OpenCRM.Infrastructure.Common");
        Assert.DoesNotContain(dependencies, item => item.Name!.StartsWith("OpenCRM.Modules.Vehicles", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(IStockCommandService).Assembly.GetReferencedAssemblies(),
            item => item.Name!.StartsWith("OpenCRM.", StringComparison.Ordinal));
    }
}
