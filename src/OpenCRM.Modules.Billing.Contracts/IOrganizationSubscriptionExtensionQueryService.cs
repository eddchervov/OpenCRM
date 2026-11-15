namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Проверяет возможность продления тарифа рабочего пространства.</summary>
public interface IOrganizationSubscriptionExtensionQueryService
{
    /// <summary> Проверяет возможность продления тарифа организации. </summary>
    Task<bool> CanExtendSubscriptionAsync(string userId, Guid organizationId);

    /// <summary> Возвращает информацию о возможности продления тарифа организации. </summary>
    Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(
        string userId,
        Guid organizationId);
}
