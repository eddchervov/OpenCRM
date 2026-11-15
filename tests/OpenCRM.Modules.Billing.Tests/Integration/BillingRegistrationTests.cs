using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions;

namespace OpenCRM.Modules.Billing.Tests.Integration;

public sealed class BillingRegistrationTests
{
    [Fact]
    public void BillingServicesAreScopedAndResolveWithValidatedScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<OpenCRMContext>(_ => null!);
        services.AddScoped<IFeatureDefaultsInitializer>(_ => Mock.Of<IFeatureDefaultsInitializer>());
        services.AddScoped<IOrganizationPermissionService>(_ => Mock.Of<IOrganizationPermissionService>());
        services.AddScoped<IOrganizationAuditService>(_ => Mock.Of<IOrganizationAuditService>());
        services.AddScoped<IWorkspaceAccessService>(_ => Mock.Of<IWorkspaceAccessService>());
        services.AddBillingExternalDependencies();
        services.AddScoped<IPaymentCreationGateway>(_ => Mock.Of<IPaymentCreationGateway>());
        // Промокоды теперь принадлежат модулю Promotions: Billing использует внешний scoped mock.
        services.AddScoped<IPromoCodeService>(_ => Mock.Of<IPromoCodeService>());

        services.AddSingleton(Mock.Of<OpenCRM.Services.Interfaces.Payments.IYooKassaService>());
        Assert.Same(services, services.AddOpenCRMBilling());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var firstService = first.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();
        var firstCatalogService = first.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCatalogQueryService>();
        var firstExtensionQueryService = first.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();
        var registered = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOrganizationEntitlementService));
        var catalog = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOrganizationSubscriptionCatalogQueryService));
        var extensionQuery = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOrganizationSubscriptionExtensionQueryService));
        var reader = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(OrganizationEntitlementReader));

        Assert.Equal(ServiceLifetime.Scoped, registered.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, catalog.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, extensionQuery.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, reader.Lifetime);
        Assert.Same(firstService, first.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>());
        Assert.NotSame(firstService, second.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>());
        Assert.NotSame(firstCatalogService, second.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCatalogQueryService>());
        Assert.NotSame(firstExtensionQueryService, second.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>());
        Assert.Equal("OpenCRM.Modules.Billing", registered.ImplementationType!.Assembly.GetName().Name);
        foreach (var serviceType in new[] { typeof(IPersonalSubscriptionQueryService),
            typeof(IPersonalSubscriptionCatalogQueryService), typeof(IPersonalSubscriptionActivationService),
            typeof(IOrganizationSubscriptionExtensionQueryService), typeof(IAdminBillingQueryService), typeof(IAdminNpdReceiptService),
            typeof(IAdminPaymentCommandService), typeof(IAdminSubscriptionCommandService),
            typeof(IPersonalSubscriptionPurchasePolicyService), typeof(IPersonalSubscriptionCheckoutService),
            typeof(IOrganizationSubscriptionCheckoutService), typeof(IOrganizationSubscriptionActivationService),
            typeof(IPersonalSubscriptionCommandService), typeof(IOrganizationSubscriptionCommandService),
            typeof(IOrganizationSubscriptionPurchasePolicyService), typeof(IFeatureAccessService),
            typeof(IOrganizationSubscriptionService), typeof(IOrganizationPromoAccessGrantPort), typeof(IFeatureAdminService),
            typeof(ISubscriptionService), typeof(ISubscriptionPlanAdminService), typeof(ISubscriptionExpiryReminderService) })
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            var instance = first.ServiceProvider.GetRequiredService(serviceType);
            Assert.Equal("OpenCRM.Modules.Billing", instance.GetType().Assembly.GetName().Name);
            Assert.Same(instance, first.ServiceProvider.GetRequiredService(serviceType));
            Assert.NotSame(instance, second.ServiceProvider.GetRequiredService(serviceType));
        }

        // Регистрация Billing не должна подменять внешнюю реализацию промокодов.
        var externalPromoCodeService = first.ServiceProvider.GetRequiredService<IPromoCodeService>();
        Assert.NotEqual("OpenCRM.Modules.Billing", externalPromoCodeService.GetType().Assembly.GetName().Name);
        Assert.Same(externalPromoCodeService, first.ServiceProvider.GetRequiredService<IPromoCodeService>());
    }

    [Fact]
    public void BillingAssembliesAndProjectsHaveNarrowDependenciesAndOnlyRegistrationAndCompatibilityFacadeArePublic()
    {
        var billingAssembly = typeof(BillingServiceCollectionExtensions).Assembly;
        var billingReferences = billingAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(billingReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common"
            or "OpenCRM.WebApp" or "OpenCRM.Modules.Organizations" or "OpenCRM.Modules.Planning");
        Assert.Equal(new[] { typeof(BillingServiceCollectionExtensions), typeof(OrganizationSubscriptionService),
            typeof(SubscriptionService), typeof(SubscriptionPlanAdminService), typeof(SubscriptionExpiryReminderService),
            typeof(FeatureAdminService), typeof(FeatureBootstrapper) }
            .OrderBy(type => type.FullName), billingAssembly.GetExportedTypes().OrderBy(type => type.FullName));

        var contractsAssembly = typeof(IOrganizationEntitlementService).Assembly;
        var contractReferences = contractsAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contractReferences, assembly => assembly.Name is "OpenCRM.Infrastructure.Common"
            or "OpenCRM.WebApp" or "OpenCRM.Infrastructure.Data" or "OpenCRM.Modules.Billing");

        var repositoryRoot = FindRepositoryRoot();
        var projectReferences = ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Billing", "OpenCRM.Modules.Billing.csproj"));
        var contractProjectReferences = ReadProjectReferences(Path.Combine(repositoryRoot,
            "src", "OpenCRM.Modules.Billing.Contracts", "OpenCRM.Modules.Billing.Contracts.csproj"));
        Assert.Equal(new[] { "Helpers", "OpenCRM.Domain", "OpenCRM.Infrastructure.Data", "OpenCRM.Modules.Billing.Contracts", "OpenCRM.Modules.Payments.Contracts", "OpenCRM.Modules.Promotions.Contracts", "OpenCRM.Modules.Vehicles.Contracts", "OpenCRM.Services.Interfaces" },
            projectReferences);
        Assert.Equal(new[] { "OpenCRM.Domain", "OpenCRM.Modules.Payments.Contracts" }, contractProjectReferences);
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
