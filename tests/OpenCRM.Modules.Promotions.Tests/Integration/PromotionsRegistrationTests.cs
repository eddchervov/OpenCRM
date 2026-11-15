using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

public sealed class PromotionsRegistrationTests
{
    [Fact]
    public void PromotionsServicesAreScopedAndResolveWithValidatedScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<OpenCRMContext>(_ => null!);
        services.AddScoped<IOrganizationPermissionService>(_ => Mock.Of<IOrganizationPermissionService>());
        services.AddScoped<IOrganizationAuditService>(_ => Mock.Of<IOrganizationAuditService>());
        services.AddScoped<IOrganizationPromoAccessGrantPort>(_ => Mock.Of<IOrganizationPromoAccessGrantPort>());
        Assert.Same(services, services.AddOpenCRMPromotions());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstService = first.ServiceProvider.GetRequiredService<IPromoCodeService>();
        var secondService = second.ServiceProvider.GetRequiredService<IPromoCodeService>();
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPromoCodeService));

        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal("OpenCRM.Modules.Promotions", firstService.GetType().Assembly.GetName().Name);
        Assert.Equal("OpenCRM.Modules.Promotions", registration.ImplementationType!.Assembly.GetName().Name);
        Assert.Same(firstService, first.ServiceProvider.GetRequiredService<IPromoCodeService>());
        Assert.NotSame(firstService, secondService);
        Assert.Equal("OpenCRM.Modules.Promotions.Contracts", typeof(IPromoCodeService).Assembly.GetName().Name);
        Assert.Equal("OpenCRM.Modules.Promotions.Contracts", typeof(PromotionPriceDto).Assembly.GetName().Name);
    }

    [Fact]
    public void PromotionsAssembliesAndProjectsHaveNarrowDependencies()
    {
        var promotionsAssembly = typeof(PromotionsServiceCollectionExtensions).Assembly;
        var promotionsReferences = promotionsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(promotionsReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common"
            or "OpenCRM.WebApp" or "OpenCRM.Modules.Billing");

        var contractsAssembly = typeof(IPromoCodeService).Assembly;
        var contractReferences = contractsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Data"
            or "OpenCRM.Services.Interfaces" or "OpenCRM.Modules.Billing" or "OpenCRM.Modules.Promotions");

        var repositoryRoot = FindRepositoryRoot();
        var projectReferences = ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Promotions", "OpenCRM.Modules.Promotions.csproj"));
        var contractProjectReferences = ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Promotions.Contracts", "OpenCRM.Modules.Promotions.Contracts.csproj"));
        Assert.Equal(new[] { "OpenCRM.Domain", "OpenCRM.Infrastructure.Data", "OpenCRM.Modules.Promotions.Contracts", "OpenCRM.Services.Interfaces" },
            projectReferences);
        Assert.Equal(new[] { "OpenCRM.Domain" }, contractProjectReferences);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenCRM.sln")))
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
