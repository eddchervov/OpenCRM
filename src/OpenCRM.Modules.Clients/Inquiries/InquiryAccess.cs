using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Inquiries;

internal sealed class InquiryAccess(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationPermissionService permissions)
{
    internal async Task EnsureReadAsync(string userId, Guid organizationId)
    {
        await EnsureSupportedScenarioAsync(organizationId);
        if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule))
        {
            throw new UnauthorizedAccessException("Раздел обращений недоступен в текущем тарифе рабочего пространства.");
        }

        await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.InquiriesView);
    }

    internal async Task<bool> CanReadOriginAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var scenarioCode = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId
                && !item.IsDeleted
                && !item.IsArchived
                && item.IsActive
                && item.Members.Any(member => member.UserId == userId))
            .Select(item => item.PrimaryScenarioCode)
            .FirstOrDefaultAsync(cancellationToken);
        if (scenarioCode != OrganizationTypeCodes.Detailing && scenarioCode != OrganizationTypeCodes.ServiceStation)
        {
            return false;
        }

        if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule))
        {
            return false;
        }

        return await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.InquiriesView);
    }

    internal async Task EnsureManageAsync(string userId, Guid organizationId)
    {
        await this.EnsureReadAsync(userId, organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.InquiriesManage);
    }

    internal Task<bool> HasPermissionAsync(string userId, Guid organizationId, string permissionCode) =>
        permissions.HasPermissionAsync(userId, organizationId, permissionCode);

    private async Task EnsureSupportedScenarioAsync(Guid organizationId)
    {
        var scenarioCode = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => item.PrimaryScenarioCode)
            .FirstOrDefaultAsync();
        if (scenarioCode != OrganizationTypeCodes.Detailing && scenarioCode != OrganizationTypeCodes.ServiceStation)
        {
            throw new UnauthorizedAccessException("Раздел обращений доступен только в детейлинге и автосервисе.");
        }
    }
}
