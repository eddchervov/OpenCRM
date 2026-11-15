using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Planning;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class PlanningRegistrationTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public void EveryPlanningPortResolvesInValidatedScopesAndUsesScopedImplementation()
    {
        using var context = fixture.CreateContext();
        using var provider = fixture.CreateServices(context);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var ports = new[]
        {
            typeof(ISchedulerQueryService),
            typeof(ISchedulerTaskCommandService),
            typeof(ISchedulerRecurrenceService),
            typeof(ISchedulerWorkflowCommandService),
            typeof(IMaintenanceSchedulerSyncService)
        };

        foreach (var port in ports)
        {
            var service = first.ServiceProvider.GetRequiredService(port);
            Assert.Same(service, first.ServiceProvider.GetRequiredService(port));
            Assert.NotSame(service, second.ServiceProvider.GetRequiredService(port));
        }
    }

    [Fact]
    public void PlanningPortsHaveScopedImplementationsAndOnlyRegistrationIsPublic()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMPlanning());
        var ports = new[]
        {
            typeof(ISchedulerQueryService),
            typeof(ISchedulerTaskCommandService),
            typeof(ISchedulerRecurrenceService),
            typeof(ISchedulerWorkflowCommandService),
            typeof(IMaintenanceSchedulerSyncService)
        };

        foreach (var port in ports)
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == port);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.Equal("OpenCRM.Modules.Planning", registration.ImplementationType!.Assembly.GetName().Name);
        }

        Assert.Equal(new[] { typeof(PlanningServiceCollectionExtensions) },
            typeof(PlanningServiceCollectionExtensions).Assembly.GetExportedTypes());
    }

    [Fact]
    public void PlanningAssembliesAndProjectsDoNotDependOnWebOrBusinessLayer()
    {
        var catalogAssembly = typeof(PlanningServiceCollectionExtensions).Assembly;
        var catalogReferences = catalogAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(catalogReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp"
            or "OpenCRM.Modules.Organizations");

        var contractsAssembly = typeof(ISchedulerQueryService).Assembly;
        var contractsReferences = contractsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractsReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common"
            or "OpenCRM.WebApp" or "OpenCRM.Infrastructure.Data" or "OpenCRM.Modules.Planning");

        var repositoryRoot = PlanningRegistrationTests.FindRepositoryRoot();
        var catalogProjectReferences = PlanningRegistrationTests.ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Planning", "OpenCRM.Modules.Planning.csproj"));
        var contractProjectReferences = PlanningRegistrationTests.ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Planning.Contracts", "OpenCRM.Modules.Planning.Contracts.csproj"));
        Assert.Equal(new[]
        {
            "Helpers",
            "OpenCRM.Domain",
            "OpenCRM.Infrastructure.Data",
            "OpenCRM.Modules.Catalog.Contracts",
            "OpenCRM.Modules.Planning.Contracts",
            "OpenCRM.Services.Interfaces"
        }, catalogProjectReferences);
        Assert.Empty(contractProjectReferences);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenCRM.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Не удалось найти корень решения OpenCRM.");
    }

    private static string[] ReadProjectReferences(string projectPath) =>
        System.Xml.Linq.XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((reference.Attribute("Include")?.Value
                ?? throw new InvalidOperationException($"В проекте {projectPath} найдена ссылка без Include."))
                .Replace('\\', Path.DirectorySeparatorChar)))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
