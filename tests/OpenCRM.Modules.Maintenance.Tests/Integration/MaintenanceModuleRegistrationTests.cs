using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Work;
using OpenCRM.Modules.Maintenance.Photos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Modules.Maintenance.Imports;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceModuleRegistrationTests
{
    [Fact]
    public void RegistrationMapsPublicWorkContractsToScopedImplementations()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMMaintenance());

        var expectedImplementations = new Dictionary<Type, Type>
        {
            [typeof(IMaintenancePhotoService)] = typeof(MaintenancePhotoService),
            [typeof(IMaintenanceClientSummaryQueryService)] = typeof(MaintenanceClientSummaryQueryService),
            [typeof(IMaintenanceWorkQueryService)] = typeof(MaintenanceWorkQueryService),
            [typeof(IMaintenanceWorkExecutionService)] = typeof(MaintenanceWorkExecutionService),
            [typeof(IMaintenanceWorkReportService)] = typeof(MaintenanceWorkReportService),
            [typeof(IMaintenanceWorkTimeService)] = typeof(MaintenanceWorkTimeService),
            [typeof(IImportBusinessRecordProcessor)] = typeof(ImportBusinessRecordProcessor)
        };

        foreach (var expected in expectedImplementations)
        {
            var descriptor = Assert.Single(services, service => service.ServiceType == expected.Key);

            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.Equal(expected.Value, descriptor.ImplementationType);
        }
    }

    [Fact]
    public void ModuleDependsOnSharedLibrariesAndContractsButNotApplicationOrFeatureImplementations()
    {
        var moduleAssembly = typeof(MaintenanceServiceCollectionExtensions).Assembly;
        var dependencies = moduleAssembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("OpenCRM.Infrastructure.Common", dependencies);
        Assert.DoesNotContain("OpenCRM.WebApp", dependencies);
        Assert.DoesNotContain("OpenCRM.Infrastructure.FileStorage", dependencies);
        Assert.DoesNotContain("OpenCRM.Modules.Inventory", dependencies);
        Assert.DoesNotContain("OpenCRM.Modules.Vehicles", dependencies);
        Assert.Equal(
            new[] { typeof(ImportBusinessRecordProcessor), typeof(MaintenanceServiceCollectionExtensions) },
            moduleAssembly.GetExportedTypes().OrderBy(type => type.FullName));

        var implementations = moduleAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract
                && type.Namespace?.StartsWith("OpenCRM.Modules.Maintenance", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(implementations);
        Assert.All(implementations, implementation => Assert.Equal(moduleAssembly, implementation.Assembly));

        var contractDependencies = typeof(IMaintenanceWorkQueryService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractDependencies, dependency => dependency.Name is
            "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp" or "OpenCRM.Modules.Maintenance");
    }
}
