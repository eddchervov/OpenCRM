using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Workspace;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Modules.Workspace.Overview;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Shell;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Export1C;
using OpenCRM.Modules.Workspace.Personal;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceRegistrationTests
{
    [Fact]
    public void ModuleRegistersItsOwnImplementationsWithScopedLifetime()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMWorkspace());

        var expected = new Dictionary<Type, Type>
        {
            [typeof(IExport1CService)] = typeof(Export1CService),
            [typeof(IHomeAnalyticsService)] = typeof(HomeAnalyticsService),
            [typeof(IWorkspaceAccessService)] = typeof(WorkspaceAccessService),
            [typeof(IWorkspaceOverviewService)] = typeof(WorkspaceOverviewService),
            [typeof(IOrganizationWorkspaceShellService)] = typeof(OrganizationWorkspaceShellService),
            [typeof(IOrganizationWorkdayQueryService)] = typeof(OrganizationWorkdayQueryService),
            [typeof(IOrganizationWorkspaceMaintenanceQueryService)] = typeof(OrganizationWorkspaceMaintenanceQueryService),
            [typeof(IOrganizationFinanceQueryService)] = typeof(OrganizationFinanceQueryService),
            [typeof(IOrganizationMaintenanceJournalQueryService)] = typeof(OrganizationMaintenanceJournalQueryService),
            [typeof(IOrganizationWorkJournalQueryService)] = typeof(OrganizationWorkJournalQueryService),
            [typeof(IOrganizationWorkspaceAnalyticsQueryService)] = typeof(OrganizationWorkspaceAnalyticsQueryService),
            [typeof(IOrganizationWorkspacePlanningQueryService)] = typeof(OrganizationWorkspacePlanningQueryService),
            [typeof(IOrganizationWorkspaceQueryService)] = typeof(OrganizationWorkspaceQueryService)
        };
        Assert.Equal(expected.Count, services.Count);
        foreach (var pair in expected)
        {
            var descriptor = Assert.Single(services, item => item.ServiceType == pair.Key);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
            Assert.Equal(pair.Value, descriptor.ImplementationType);
        }
    }
}
