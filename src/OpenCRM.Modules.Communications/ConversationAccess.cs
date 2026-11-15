using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Communications;

internal sealed class ConversationAccess(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationPermissionService permissions)
{
    private const string ViewPermission = "messages.view";
    private const string SendPermission = "messages.send";
    private const string ManagePermission = "messages.manage";
    private const string TemplatesManagePermission = "messages.templates.manage";

    public async Task EnsureViewAsync(string userId, Guid organizationId)
    {
        await this.EnsureOrganizationMemberAsync(userId, organizationId);
        if (!await subscriptions.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule))
        {
            throw new UnauthorizedAccessException("Раздел сообщений недоступен в текущем тарифе рабочего пространства.");
        }

        await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, ViewPermission);
    }

    public async Task EnsureSendAsync(string userId, Guid organizationId)
    {
        await this.EnsureViewAsync(userId, organizationId);
        await this.EnsureClientMessagingAsync(userId, organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, SendPermission);
    }

    public async Task EnsureManageAsync(string userId, Guid organizationId)
    {
        await this.EnsureViewAsync(userId, organizationId);
        await this.EnsureClientMessagingAsync(userId, organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, ManagePermission);
    }

    public async Task EnsureTemplatesManageAsync(string userId, Guid organizationId)
    {
        await this.EnsureViewAsync(userId, organizationId);
        await this.EnsureClientMessagingAsync(userId, organizationId);
        await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, TemplatesManagePermission);
    }

    public async Task EnsureTemplatesViewAsync(string userId, Guid organizationId)
    {
        await this.EnsureViewAsync(userId, organizationId);
        await this.EnsureClientMessagingAsync(userId, organizationId);
    }

    public async Task<bool> HasClientMessagingAccessForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var ownerUserId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId
                && item.IsActive
                && !item.IsDeleted
                && !item.IsArchived)
            .Select(item => item.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);

        return ownerUserId != null
            && await subscriptions.HasFeatureAsync(ownerUserId, organizationId, Helpers.FeatureCodes.ClientMessaging);
    }

    public Task<bool> HasPermissionAsync(string userId, Guid organizationId, string permissionCode) =>
        permissions.HasPermissionAsync(userId, organizationId, permissionCode);

    public async Task<bool> IsWorkspaceReadOnlyAsync(string userId, Guid organizationId) =>
        (await subscriptions.GetWorkspaceAccessAsync(userId, organizationId)).IsReadOnly;

    private async Task EnsureOrganizationMemberAsync(string userId, Guid organizationId)
    {
        var scenarioCode = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId
                && item.IsActive
                && !item.IsArchived
                && !item.IsDeleted
                && item.Members.Any(member => member.UserId == userId))
            .Select(item => item.PrimaryScenarioCode)
            .FirstOrDefaultAsync();
        if (scenarioCode != OrganizationTypeCodes.Detailing && scenarioCode != OrganizationTypeCodes.ServiceStation)
        {
            throw new UnauthorizedAccessException("Переписка недоступна в этом рабочем пространстве.");
        }
    }

    private async Task EnsureClientMessagingAsync(string userId, Guid organizationId)
    {
        if (!await this.CanUseClientMessagingAsync(userId, organizationId))
        {
            throw new InvalidOperationException("Переписка и боты клиентов доступны по подписке PRO.");
        }
    }

    /// <summary> Проверяет тариф для новых сообщений, сохраняя чтение истории. </summary>
    internal Task<bool> CanUseClientMessagingAsync(string userId, Guid organizationId) =>
        subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.ClientMessaging);

    internal async Task EnsurePermissionAsync(string userId, Guid organizationId, string permissionCode)
    {
        await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, permissionCode);
    }
}
