using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Infrastructure.FileStorage.Media;

/// <summary> Проверка доступа пользователя к рабочему пространству медиафайлов. </summary>
internal sealed class MediaWorkspaceAccess(
    OpenCRMContext context,
    IOrganizationSubscriptionService organizationSubscriptionService) : IMediaWorkspaceAccess
{
    /// <summary> Проверяет право пользователя на чтение рабочего пространства. </summary>
    public async Task EnsureCanReadWorkspaceAsync(string userId, Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        var isMember = await context.Organizations
            .AnyAsync(x => x.Id == organizationId.Value
                && !x.IsDeleted
                && x.IsActive
                && !x.IsArchived
                && x.Members.Any(member => member.UserId == userId));

        if (!isMember)
        {
            throw new InvalidOperationException("У вас нет доступа к этой организации.");
        }
    }

    /// <summary> Проверяет право пользователя на запись в рабочее пространство. </summary>
    public async Task EnsureCanWriteWorkspaceAsync(string userId, Guid? organizationId, string? permissionCode = null)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        var roleCode = await context.Organizations
            .Where(x => x.Id == organizationId.Value && !x.IsDeleted && x.IsActive && !x.IsArchived)
            .SelectMany(x => x.Members.Where(member => member.UserId == userId))
            .Select(x => x.RoleCode)
            .FirstOrDefaultAsync();

        if (roleCode is null)
        {
            throw new InvalidOperationException("У вас нет доступа к этой организации.");
        }

        if (roleCode == OrganizationRoleCodes.Viewer)
        {
            throw new InvalidOperationException("В этой организации у вас доступ только на просмотр.");
        }

        if (permissionCode != null)
        {
            await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId.Value, permissionCode);

            return;
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId.Value);
    }
}
