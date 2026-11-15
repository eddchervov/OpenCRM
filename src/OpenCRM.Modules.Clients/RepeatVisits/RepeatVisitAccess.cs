using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.RepeatVisits;

internal sealed class RepeatVisitAccess(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationPermissionService permissions)
{
    internal async Task EnsureReadAsync(string userId, Guid organizationId)
    {
        await EnsureSupportedScenarioAsync(organizationId);
        if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule))
        {
            throw new UnauthorizedAccessException("Раздел клиентов недоступен в текущем тарифе рабочего пространства.");
        }

        try
        {
            await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
        }
        catch (InvalidOperationException exception)
        {
            throw new UnauthorizedAccessException(exception.Message, exception);
        }
        await EnsurePermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView);
        await EnsurePermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
    }

    internal async Task EnsureWriteAsync(string userId, Guid organizationId)
    {
        await EnsureReadAsync(userId, organizationId);
        try
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);
        }
        catch (InvalidOperationException exception)
        {
            throw new UnauthorizedAccessException(exception.Message, exception);
        }
    }

    internal async Task<bool> CanWriteAsync(string userId, Guid organizationId, string permissionCode)
    {
        try
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, permissionCode);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal async Task EnsureContactActionWriteAsync(string userId, Guid organizationId)
    {
        await EnsureReadAsync(userId, organizationId);
        try
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
        }
        catch (InvalidOperationException exception)
        {
            throw new UnauthorizedAccessException(exception.Message, exception);
        }
    }

    internal Task<bool> HasPermissionAsync(string userId, Guid organizationId, string permissionCode) =>
        permissions.HasPermissionAsync(userId, organizationId, permissionCode);

    private async Task EnsurePermissionAsync(string userId, Guid organizationId, string permissionCode)
    {
        if (!await permissions.HasPermissionAsync(userId, organizationId, permissionCode))
        {
            throw new UnauthorizedAccessException("Недостаточно прав для просмотра источника рекомендации.");
        }
    }

    private async Task EnsureSupportedScenarioAsync(Guid organizationId)
    {
        var scenarioCode = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive)
            .Select(item => item.PrimaryScenarioCode)
            .FirstOrDefaultAsync();
        if (scenarioCode != OrganizationTypeCodes.Detailing && scenarioCode != OrganizationTypeCodes.ServiceStation)
        {
            throw new UnauthorizedAccessException("Повторные визиты доступны только в детейлинге и автосервисе.");
        }
    }
}
