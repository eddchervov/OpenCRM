using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Workspace.Export1C;
using OpenCRM.Modules.Workspace.Personal;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Modules.Workspace.Overview;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Shell;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Workspace;

/// <summary> Расширения коллекции сервисов модуля рабочего пространства. </summary>
public static class WorkspaceServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля рабочего пространства. </summary>
    public static IServiceCollection AddOpenCRMWorkspace(this IServiceCollection services)
    {
        services.AddScoped<IExport1CService, Export1CService>();
        services.AddScoped<IHomeAnalyticsService, HomeAnalyticsService>();
        services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
        services.AddScoped<IWorkspaceOverviewService, WorkspaceOverviewService>();
        services.AddScoped<IOrganizationWorkspaceShellService, OrganizationWorkspaceShellService>();
        services.AddScoped<IOrganizationWorkdayQueryService, OrganizationWorkdayQueryService>();
        services.AddScoped<IOrganizationWorkspaceMaintenanceQueryService, OrganizationWorkspaceMaintenanceQueryService>();
        services.AddScoped<IOrganizationFinanceQueryService, OrganizationFinanceQueryService>();
        services.AddScoped<IOrganizationMaintenanceJournalQueryService, OrganizationMaintenanceJournalQueryService>();
        services.AddScoped<IOrganizationWorkJournalQueryService, OrganizationWorkJournalQueryService>();
        services.AddScoped<IOrganizationWorkspaceAnalyticsQueryService, OrganizationWorkspaceAnalyticsQueryService>();
        services.AddScoped<IOrganizationWorkspacePlanningQueryService, OrganizationWorkspacePlanningQueryService>();
        services.AddScoped<IOrganizationWorkspaceQueryService, OrganizationWorkspaceQueryService>();

        return services;
    }
}
