using Helpers;
using OpenCRM.Domain;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Access;

/// <summary> Сервис проверки доступа к рабочему пространству организации. </summary>
public sealed class WorkspaceAccessService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationEntitlementService organizationEntitlementService,
    IOrganizationPermissionService organizationPermissionService,
    IPlatformCommercialPolicy platformCommercialPolicy) : IWorkspaceAccessService
{
    /// <summary> Возвращает сводку доступа к разделам рабочего пространства. </summary>
    public async Task<WorkspaceOverviewAccessDto> GetOverviewAsync(string userId, Guid organizationId)
    {
        var workspaceAccess = await this.GetWorkspaceAccessAsync(userId, organizationId);
        var featureSnapshot = await organizationEntitlementService.GetFeatureSnapshotAsync(userId, organizationId);
        var hasVehiclesModule = featureSnapshot.Features.Any(feature => feature.Code == FeatureCodes.VehiclesModule && feature.BoolValue == true);
        var hasClientsModule = featureSnapshot.Features.Any(feature => feature.Code == FeatureCodes.ClientsModule && feature.BoolValue == true);
        var canViewVehicles = hasVehiclesModule
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView);

        var canViewAnalytics = await organizationPermissionService.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.AnalyticsView);

        var canViewClients = hasClientsModule
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);

        return new WorkspaceOverviewAccessDto
        {
            WorkspaceAccess = workspaceAccess,
            FeatureSnapshot = featureSnapshot,
            HasDefectsModule = featureSnapshot.Features.Any(feature => feature.Code == FeatureCodes.DefectsModule && feature.BoolValue == true),
            HasCountersModule = featureSnapshot.Features.Any(feature => feature.Code == FeatureCodes.CountersModule && feature.BoolValue == true),
            HasSchedulerModule = featureSnapshot.Features.Any(feature => feature.Code == FeatureCodes.SchedulerModule && feature.BoolValue == true),
            CanViewVehicles = canViewVehicles,
            CanViewAnalytics = canViewAnalytics,
            CanViewClients = canViewClients
        };
    }

    /// <summary> Возвращает права доступа пользователя к рабочему пространству. </summary>
    public async Task<OrganizationWorkspaceAccessDto> GetWorkspaceAccessAsync(string userId, Guid organizationId)
    {
        var entitlement = await organizationEntitlementService.GetWorkspaceEntitlementAsync(userId, organizationId);

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId && !x.IsDeleted && !x.IsArchived && x.IsActive)
            .Select(x => new
            {
                x.Id,
                x.OwnerUserId,
                RoleCode = x.Members
                    .Where(member => member.UserId == userId)
                    .Select(member => member.RoleCode)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Организация не найдена.");

        if (organization.RoleCode is null)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        var commercialEnabled = platformCommercialPolicy.CommercialEnabled;
        var isBaseWorkspace = entitlement.IsBaseWorkspace;
        var hasActiveSubscription = entitlement.HasActiveSubscription;
        var hadBusinessSubscription = entitlement.HasSubscriptionHistory;

        var isViewer = organization.RoleCode == OrganizationRoleCodes.Viewer;

        // В нейтральном режиме ограничения по тарифу не применяются:
        // доступ определяется членством и правами роли, а не подпиской.
        var isFreeMember = commercialEnabled && entitlement.IsFreePlan && organization.OwnerUserId != userId;
        var isReadOnly = isViewer
            || isFreeMember
            || (commercialEnabled && !hasActiveSubscription && !entitlement.IsFreePlan);
        var exportsEnabled = (!commercialEnabled || hasActiveSubscription || entitlement.IsFreePlan)
            && await organizationPermissionService.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.DocumentsExport);
        var canEditVehicles = !isReadOnly
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesEdit);
        var canArchiveVehicles = !isReadOnly
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesArchive);
        string? readOnlyMessage = null;

        if (isViewer)
        {
            readOnlyMessage = "В этой организации у вас доступ только на просмотр.";
        }
        else if (isFreeMember)
        {
            readOnlyMessage = "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.";
        }
        else if (isReadOnly)
        {
            readOnlyMessage = hadBusinessSubscription
                ? "Срок бизнес-тарифа закончился. Подключите новый тариф, чтобы снова изменять данные."
                : "Данные доступны для просмотра. Добавление и изменение записей — на демо или бизнес-тарифе.";

            if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage))
            {
                readOnlyMessage = hadBusinessSubscription
                    ? "Срок тарифа закончился. Данные доступны только для просмотра."
                    : "В организации не подключён тариф. Данные доступны только для просмотра.";
            }
            else if (!hadBusinessSubscription)
            {
                var demoAvailability = await organizationEntitlementService.CheckDemoAvailabilityAsync(userId, organizationId);
                if (demoAvailability.Available)
                {
                    readOnlyMessage += " Можно подключить демо.";
                }
            }
        }

        return new OrganizationWorkspaceAccessDto
        {
            CommercialEnabled = commercialEnabled,
            IsFreePlan = entitlement.IsFreePlan,
            IsBaseWorkspace = isBaseWorkspace,
            HasActiveSubscription = hasActiveSubscription,
            IsReadOnly = isReadOnly,
            ExportsEnabled = exportsEnabled,
            CanEditVehicles = canEditVehicles,
            CanArchiveVehicles = canArchiveVehicles,
            RoleCode = organization.RoleCode,
            ReadOnlyMessage = readOnlyMessage
        };
    }

    /// <summary> Проверяет право пользователя на чтение раздела рабочего пространства. </summary>
    public async Task EnsureWorkspaceCanReadAsync(string userId, Guid organizationId, string permissionCode)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await this.EnsureMembershipAsync(userId, organizationId);

        if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, permissionCode))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(permissionCode));
        }

    }

    /// <summary> Проверяет право пользователя на запись в рабочее пространство. </summary>
    public async Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId)
    {
        await this.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView);
    }

    /// <summary> Проверяет право пользователя на запись в раздел рабочего пространства. </summary>
    public async Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId, string permissionCode)
    {
        var access = await this.GetWorkspaceAccessAsync(userId, organizationId);
        if (access.IsReadOnly)
        {
            throw new InvalidOperationException(access.ReadOnlyMessage);
        }

        if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, permissionCode))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(permissionCode));
        }

        var requiredFeature = permissionCode switch
        {
            OrganizationPermissionCodes.EmployeesManage or OrganizationPermissionCodes.ShiftsManage
                or OrganizationPermissionCodes.MembersInvite or OrganizationPermissionCodes.MembersRolesManage
                or OrganizationPermissionCodes.MaintenanceWorkExecute => FeatureCodes.MembersModule,
            OrganizationPermissionCodes.MaterialsManage => FeatureCodes.MaterialsModule,
            OrganizationPermissionCodes.DefectsCreate or OrganizationPermissionCodes.DefectsEdit
                or OrganizationPermissionCodes.DefectsDelete or OrganizationPermissionCodes.DefectsStatusManage
                or OrganizationPermissionCodes.DefectsMaintenanceLink => FeatureCodes.DefectsModule,
            _ => null
        };

        if (requiredFeature is not null
            && !await organizationEntitlementService.HasFeatureAsync(userId, organizationId, requiredFeature))
        {
            throw new InvalidOperationException("Эта возможность доступна в PRO.");
        }
    }

    /// <summary> Проверяет членство пользователя в организации. </summary>
    private async Task EnsureMembershipAsync(string userId, Guid organizationId)
    {
        var canRead = await context.Organizations
            .AsNoTracking()
            .AnyAsync(x => x.Id == organizationId
                && !x.IsDeleted
                && !x.IsArchived
                && x.IsActive
                && x.Members.Any(member => member.UserId == userId));

        if (!canRead)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }
    }
}
