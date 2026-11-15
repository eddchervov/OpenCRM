using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Brands;
using OpenCRM.Modules.Catalog.Models;
using OpenCRM.Services.Interfaces.Brands;
using OpenCRM.Services.Interfaces.Models;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

public sealed class CatalogRegistrationTests
{
    [Fact]
    public void CatalogPortsHaveScopedImplementationsAndOnlyRegistrationIsPublic()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMCatalog());
        var ports = new[]
        {
            typeof(ICatalogQueryService),
            typeof(ICatalogCommandService),
            typeof(ICatalogInitializationService),
            typeof(ICatalogSetupService),
            typeof(ICatalogMaterialLookup)
        };

        foreach (var port in ports)
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == port);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.Equal("OpenCRM.Modules.Catalog", registration.ImplementationType!.Assembly.GetName().Name);
        }

        foreach (var port in new[] { typeof(IBrandService), typeof(IModelService) })
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == port);
            Assert.Equal(ServiceLifetime.Transient, registration.Lifetime);
            Assert.Equal("OpenCRM.Modules.Catalog", registration.ImplementationType!.Assembly.GetName().Name);
        }

        Assert.Equal(new[] { typeof(CatalogServiceCollectionExtensions), typeof(BrandService), typeof(ModelService) }
            .OrderBy(type => type.FullName),
            typeof(CatalogServiceCollectionExtensions).Assembly.GetExportedTypes().OrderBy(type => type.FullName));
    }

    [Fact]
    public void CatalogAssembliesAndProjectsDoNotDependOnWebOrBusinessLayer()
    {
        var catalogAssembly = typeof(CatalogServiceCollectionExtensions).Assembly;
        var catalogReferences = catalogAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(catalogReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp"
            or "OpenCRM.Modules.Organizations");

        var contractsAssembly = typeof(ICatalogQueryService).Assembly;
        var contractsReferences = contractsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractsReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common"
            or "OpenCRM.WebApp" or "OpenCRM.Infrastructure.Data" or "OpenCRM.Modules.Catalog");

        var repositoryRoot = CatalogRegistrationTests.FindRepositoryRoot();
        var catalogProjectReferences = CatalogRegistrationTests.ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Catalog", "OpenCRM.Modules.Catalog.csproj"));
        var contractProjectReferences = CatalogRegistrationTests.ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Catalog.Contracts", "OpenCRM.Modules.Catalog.Contracts.csproj"));
        Assert.Equal(new[]
        {
            "OpenCRM.Domain",
            "OpenCRM.Infrastructure.Data",
            "OpenCRM.Modules.Catalog.Contracts",
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
