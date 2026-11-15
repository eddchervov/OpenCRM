using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.Extensions.Configuration;
using OpenCRM.Modules.Clients.Contracts;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.WebApp.Extensions;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceCompositionTests
{
    [Fact]
    public void ProductionCompositionResolvesWorkspaceQueriesWithoutReplacingTheirDependencies()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "workspace-appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OpenCRMConnection"] = "Host=127.0.0.1;Database=composition_test;Username=test;Password=test",
            ["DataProtection:KeysDirectory"] = Path.GetTempPath(),
            ["LocalTesting:UseInMemoryNotifications"] = "true",
            ["S3:ServiceUrl"] = "http://127.0.0.1:9000",
            ["S3:BucketName"] = "composition-test",
            ["S3:AccessKey"] = "test-access-key",
            ["S3:SecretKey"] = "test-secret-key"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOpenCRMDataAccess(configuration);
        services.AddOpenCRMIdentity(configuration);
        services.AddOpenCRMPresentation(Helpers.ProjectBrand.Read(key => configuration[key]));
        services.AddOpenCRMInfrastructure(configuration);
        var queryTypes = new[]
        {
            typeof(OpenCRM.Services.Interfaces.Imports.IOrganizationImportService),
            typeof(IOrganizationSubscriptionActivationService),
            typeof(IOrganizationSubscriptionPurchasePolicyService),
            typeof(IPersonalSubscriptionActivationService),
            typeof(IPersonalSubscriptionPurchasePolicyService),
            typeof(IPersonalSubscriptionQueryService),
            typeof(IPersonalSubscriptionCatalogQueryService),
            typeof(OpenCRM.Services.Interfaces.Features.IFeatureAccessService),
            typeof(IOrganizationEntitlementService),
            typeof(IOrganizationSubscriptionCatalogQueryService),
            typeof(IOrganizationSubscriptionService),
            typeof(IWorkspaceAccessService),
            typeof(IWorkspaceOverviewService),
            typeof(IOrganizationWorkdayQueryService),
            typeof(IOrganizationWorkspaceMaintenanceQueryService),
            typeof(IOrganizationFinanceQueryService),
            typeof(IOrganizationMaintenanceJournalQueryService),
            typeof(IOrganizationWorkJournalQueryService),
            typeof(IOrganizationWorkspaceAnalyticsQueryService),
            typeof(IOrganizationWorkspacePlanningQueryService),
            typeof(IOrganizationWorkspaceQueryService),
            typeof(IVehicleCounterQueryService),
            typeof(IVehicleCounterCommandService),
            typeof(IVehicleQueryService),
            typeof(IVehicleMaintenanceLookupService),
            typeof(ICatalogMaterialLookup),
            typeof(ISubscriptionService),
            typeof(IVehicleCommandService),
            typeof(IVehicleLimitQueryService),
            typeof(IVehicleDocumentHistoryService),
            typeof(IInventoryQueryService),
            typeof(IMaintenanceQueryService),
            typeof(IMaintenanceSlotQueryService),
            typeof(IOrganizationClientQueryService),
            typeof(IOrganizationClientDetailsQueryService),
            typeof(IOrganizationClientCommandService),
            typeof(IOrganizationClientVehicleLinkService),
            typeof(IOrganizationQueryService),
            typeof(IOrganizationProfileCommandService),
            typeof(IOrganizationArchiveService),
            typeof(IOrganizationMembershipService),
            typeof(IOrganizationInvitationQueryService),
            typeof(IOrganizationInvitationCommandService),
            typeof(IOrganizationSetupService),
            typeof(IOrganizationClientCountQueryService)
        };
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        foreach (var queryType in queryTypes)
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == queryType);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            var instance = first.ServiceProvider.GetRequiredService(queryType);

            var expectedAssembly = queryType.Name is nameof(IWorkspaceAccessService)
                or nameof(IWorkspaceOverviewService)
                or nameof(IOrganizationWorkdayQueryService)
                or nameof(IOrganizationWorkspaceMaintenanceQueryService)
                or nameof(IOrganizationFinanceQueryService)
                or nameof(IOrganizationMaintenanceJournalQueryService)
                or nameof(IOrganizationWorkJournalQueryService)
                or nameof(IOrganizationWorkspaceAnalyticsQueryService)
                or nameof(IOrganizationWorkspacePlanningQueryService)
                or nameof(IOrganizationWorkspaceQueryService)
                or nameof(IOrganizationWorkspaceShellService)
                ? "OpenCRM.Modules.Workspace"
                : queryType.Namespace switch
            {
                "OpenCRM.Services.Interfaces.Imports" => "OpenCRM.Modules.Imports",
                "OpenCRM.Modules.Billing.Contracts" => "OpenCRM.Modules.Billing",
                "OpenCRM.Modules.Catalog.Contracts" => "OpenCRM.Modules.Catalog",
                "OpenCRM.Services.Interfaces.Subscriptions" => "OpenCRM.Modules.Billing",
                "OpenCRM.Services.Interfaces.Features" => "OpenCRM.Modules.Billing",
                "OpenCRM.Services.Interfaces.Organizations" when queryType == typeof(IOrganizationSubscriptionService)
                    => "OpenCRM.Modules.Billing",
                "OpenCRM.Modules.Vehicles.Contracts" => "OpenCRM.Modules.Vehicles",
                "OpenCRM.Modules.Inventory.Contracts" => "OpenCRM.Modules.Inventory",
                "OpenCRM.Modules.Maintenance.Contracts" => "OpenCRM.Modules.Maintenance",
                "OpenCRM.Modules.Clients.Contracts" => "OpenCRM.Modules.Clients",
                "OpenCRM.Modules.Organizations.Contracts" => "OpenCRM.Modules.Organizations",
                _ => "OpenCRM.Infrastructure.Common"
            };
            Assert.Equal(expectedAssembly, instance.GetType().Assembly.GetName().Name);
            Assert.Same(instance, first.ServiceProvider.GetRequiredService(queryType));
            Assert.NotSame(instance, second.ServiceProvider.GetRequiredService(queryType));
        }
    }
}
