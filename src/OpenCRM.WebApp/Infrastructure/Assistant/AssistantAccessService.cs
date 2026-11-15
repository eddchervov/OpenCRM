using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantAccessService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationPermissionService permissions,
    AssistantOptions options) : IAssistantExecutionGuard
{
    private static readonly HashSet<string> s_readActions = new(StringComparer.Ordinal)
    {
        "maintenance_get", "vehicle_get", "client_get", "catalog_list", "location_list", "employee_list",
        "shift_list", "inventory_list", "defect_list", "report", "schedule"
    };

    public async Task<AssistantSettings> EnsureAvailableAsync(AssistantActor actor, CancellationToken cancellationToken)
    {
        if (actor.Channel is not ("web" or "telegram") || string.IsNullOrWhiteSpace(actor.UserId) || actor.OrganizationId == Guid.Empty)
        {
            throw new InvalidOperationException("Не определены сотрудник и организация помощника.");
        }

        var settings = await context.Set<AssistantSettings>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        var access = await context.Set<AssistantOrganizationAccess>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == actor.OrganizationId, cancellationToken);
        if (settings is null || !settings.IsEnabled || access is null || !access.IsEnabled ||
            access.EnabledUntil.HasValue && access.EnabledUntil <= DateTime.UtcNow ||
            actor.Channel == "web" && (!settings.WebEnabled || !access.WebEnabled) ||
            actor.Channel == "telegram" && (!settings.TelegramEnabled || !access.TelegramEnabled))
        {
            throw new InvalidOperationException("Помощник отключён или недоступен для этой организации.");
        }

        if (!await context.Organizations.AnyAsync(item => item.Id == actor.OrganizationId && options.AllowedScenarioCodes.Contains(item.PrimaryScenarioCode), cancellationToken))
        {
            throw new InvalidOperationException("Это направление пока не подключено к помощнику.");
        }

        if (!await subscriptions.HasFeatureAsync(actor.UserId, actor.OrganizationId, Helpers.FeatureCodes.Assistant))
        {
            throw new InvalidOperationException("ИИ-помощник недоступен для этого рабочего пространства.");
        }

        await subscriptions.EnsureWorkspaceCanReadAsync(actor.UserId, actor.OrganizationId, OrganizationPermissionCodes.WorkspaceView);

        return settings;
    }

    public async Task EnsureHistoryAvailableAsync(AssistantActor actor, CancellationToken cancellationToken)
    {
        if (actor.Channel is not ("web" or "telegram") || string.IsNullOrWhiteSpace(actor.UserId) || actor.OrganizationId == Guid.Empty)
        {
            throw new InvalidOperationException("Не определены сотрудник и организация помощника.");
        }

        if (!await context.Organizations.AnyAsync(item => item.Id == actor.OrganizationId
            && options.AllowedScenarioCodes.Contains(item.PrimaryScenarioCode), cancellationToken))
        {
            throw new InvalidOperationException("Это направление пока не подключено к помощнику.");
        }

        await subscriptions.EnsureWorkspaceCanReadAsync(actor.UserId, actor.OrganizationId, OrganizationPermissionCodes.WorkspaceView);
    }

    public async Task EnsureAllowedAsync(AssistantActor actor, string action, CancellationToken cancellationToken)
    {
        await this.EnsureAvailableAsync(actor, cancellationToken);
        if (!AiPilotActionCatalog.TryGet(action, out _) || action == "unsupported")
        {
            throw new InvalidOperationException("Это действие помощнику недоступно.");
        }

        var permission = PermissionFor(action);
        if (IsReadOnly(action))
        {
            await subscriptions.EnsureWorkspaceCanReadAsync(actor.UserId, actor.OrganizationId, permission);
        }
        else
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(actor.UserId, actor.OrganizationId, permission);
        }
    }

    public async Task<IReadOnlyList<AiPilotActionDefinition>> GetActionsAsync(AssistantActor actor, CancellationToken cancellationToken)
    {
        await this.EnsureAvailableAsync(actor, cancellationToken);
        var access = await subscriptions.GetWorkspaceAccessAsync(actor.UserId, actor.OrganizationId);
        var decisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        var actions = new List<AiPilotActionDefinition>();
        foreach (var action in AiPilotActionCatalog.All)
        {
            if (action.Action == "unsupported")
            {
                actions.Add(action);
                continue;
            }

            if (access.IsReadOnly && !IsReadOnly(action.Action))
            {
                continue;
            }

            var permission = PermissionFor(action.Action);
            if (!decisions.TryGetValue(permission, out var allowed))
            {
                allowed = await permissions.HasPermissionAsync(actor.UserId, actor.OrganizationId, permission);
                decisions[permission] = allowed;
            }

            if (allowed)
            {
                actions.Add(action);
            }
        }

        return actions;
    }

    public static bool IsReadOnly(string action) => s_readActions.Contains(action);

    public static string PermissionFor(string action) => action switch
    {
        "visit" or "book" or "arrive" => OrganizationPermissionCodes.MaintenanceCreate,
        "maintenance_get" or "schedule" => OrganizationPermissionCodes.MaintenanceView,
        "maintenance_delete" => OrganizationPermissionCodes.MaintenanceDelete,
        "vehicle_get" => OrganizationPermissionCodes.VehiclesView,
        "vehicle_create" => OrganizationPermissionCodes.VehiclesCreate,
        "vehicle_update" => OrganizationPermissionCodes.VehiclesEdit,
        "vehicle_archive" or "vehicle_restore" => OrganizationPermissionCodes.VehiclesArchive,
        "counter_add" or "counter_remove" => OrganizationPermissionCodes.VehiclesCountersManage,
        "client_get" => OrganizationPermissionCodes.ClientsView,
        "client_create" => OrganizationPermissionCodes.ClientsCreate,
        "client_update" or "client_comment" => OrganizationPermissionCodes.ClientsEdit,
        "catalog_list" or "location_list" => OrganizationPermissionCodes.WorkspaceCatalogView,
        "catalog_create" or "catalog_update" or "catalog_delete" or "location_create" or "location_update" or "location_delete" => OrganizationPermissionCodes.WorkspaceProfileEdit,
        "employee_list" or "employee_create" or "employee_update" or "employee_deactivate" or "employee_link" => OrganizationPermissionCodes.EmployeesManage,
        "shift_list" => OrganizationPermissionCodes.ShiftsView,
        "shift_create" or "shift_update" or "shift_delete" or "shift_publish" or "shift_copy" => OrganizationPermissionCodes.ShiftsManage,
        "inventory_list" => OrganizationPermissionCodes.MaterialsView,
        "inventory_receive" or "inventory_adjust" or "inventory_issue" => OrganizationPermissionCodes.MaterialsManage,
        "defect_list" => OrganizationPermissionCodes.DefectsView,
        "defect_create" => OrganizationPermissionCodes.DefectsCreate,
        "defect_update" => OrganizationPermissionCodes.DefectsEdit,
        "defect_close" => OrganizationPermissionCodes.DefectsStatusManage,
        "defect_delete" => OrganizationPermissionCodes.DefectsDelete,
        "work_start" or "work_pause" or "work_finish" or "work_time" or "work_report" or "material_add" or "material_remove" => OrganizationPermissionCodes.MaintenanceWorkExecute,
        "payment_add" or "payment_cancel" => OrganizationPermissionCodes.FinanceManage,
        "report" => OrganizationPermissionCodes.FinanceView,
        _ => OrganizationPermissionCodes.MaintenanceEdit
    };
}
