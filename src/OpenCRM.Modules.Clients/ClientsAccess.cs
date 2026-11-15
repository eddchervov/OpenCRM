using Helpers;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients;

/// <summary> Проверка доступа к функциональности клиентов организации. </summary>
internal sealed class ClientsAccess(IOrganizationSubscriptionService subscription)
{
    /// <summary> Зависимость сервиса подписок организаций. </summary>
    private readonly IOrganizationSubscriptionService _subscription = subscription;

    /// <summary> Проверяет доступность модуля клиентов для организации. </summary>
    internal async Task EnsureEnabledAsync(string userId, Guid organizationId)
    {
        var hasClientsModule = await this._subscription.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule);
        if (!hasClientsModule)
        {
            throw new InvalidOperationException("Раздел клиентов недоступен в текущем тарифе рабочего пространства.");
        }

        await this._subscription.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
    }
}
