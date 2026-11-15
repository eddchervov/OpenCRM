using Helpers;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Проверяет доступ пользователя к рабочему пространству обслуживания. </summary>
internal sealed class MaintenanceAccess(
    IOrganizationSubscriptionService organizationSubscriptionService)
{
    /// <summary> Проверяет доступ к созданию и изменению расширенной приёмки. </summary>
    internal async Task EnsureIntakeCanWriteAsync(string userId, Guid organizationId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId);
        if (!await organizationSubscriptionService.HasFeatureAsync(userId, organizationId, FeatureCodes.MaintenanceIntake))
        {
            throw new InvalidOperationException("Расширенная приёмка автомобиля доступна в PRO.");
        }
    }

    /// <summary> Проверяет право пользователя читать рабочее пространство. </summary>
    internal async Task EnsureCanReadWorkspaceAsync(string userId, Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId.Value, OrganizationPermissionCodes.MaintenanceView);
    }

    /// <summary> Проверяет право пользователя изменять рабочее пространство. </summary>
    internal async Task EnsureCanWriteWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.MaintenanceEdit)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId.Value, permissionCode);
    }
}
