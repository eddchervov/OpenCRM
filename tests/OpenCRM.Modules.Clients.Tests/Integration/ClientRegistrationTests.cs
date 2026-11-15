using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Queries;

namespace OpenCRM.Modules.Clients.Tests.Integration;

public sealed class ClientRegistrationTests
{
    [Fact]
    public void RegistrationMapsEachPublicClientPortToAScopedImplementation()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMClients());

        var expected = new Dictionary<Type, string>
        {
            [typeof(IOrganizationClientQueryService)] = "OpenCRM.Modules.Clients.Queries.OrganizationClientQueryService",
            [typeof(IOrganizationClientCountQueryService)] = "OpenCRM.Modules.Clients.Queries.OrganizationClientCountQueryService",
            [typeof(IOrganizationClientDetailsQueryService)] = "OpenCRM.Modules.Clients.Queries.OrganizationClientDetailsQueryService",
            [typeof(IOrganizationClientCommandService)] = "OpenCRM.Modules.Clients.Commands.OrganizationClientCommandService",
            [typeof(IOrganizationClientVehicleLinkService)] = "OpenCRM.Modules.Clients.Commands.OrganizationClientVehicleLinkService",
            [typeof(IQuickBookingClientLookupService)] = "OpenCRM.Modules.Clients.Queries.QuickBookingClientLookupService"
        };

        foreach (var registration in expected)
        {
            var descriptor = Assert.Single(services, item => item.ServiceType == registration.Key);

            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.Equal(registration.Value, descriptor.ImplementationType?.FullName);
        }
    }

    [Fact]
    public void ModuleAndContractsDoNotDependOnApplicationOrNeighborImplementations()
    {
        var moduleAssembly = typeof(ClientsServiceCollectionExtensions).Assembly;
        var moduleDependencies = moduleAssembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("OpenCRM.Infrastructure.Common", moduleDependencies);
        Assert.DoesNotContain("OpenCRM.WebApp", moduleDependencies);
        Assert.DoesNotContain("OpenCRM.Modules.Maintenance", moduleDependencies);
        Assert.DoesNotContain("OpenCRM.Modules.Inventory", moduleDependencies);
        Assert.DoesNotContain("OpenCRM.Modules.Vehicles", moduleDependencies);
        Assert.Equal(
            new[] { typeof(ClientsServiceCollectionExtensions), typeof(QuickBookingClientLookupService) },
            moduleAssembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal));

        var contractDependencies = typeof(IOrganizationClientQueryService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractDependencies, dependency => dependency.Name is
            "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp" or "OpenCRM.Modules.Clients");
    }
}
