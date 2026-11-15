using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Чтение личных подписок, истории и тарифных ограничений без выполнения платёжных команд.
/// </summary>
public interface IPersonalSubscriptionQueryService
{
    /// <summary> Возвращает текущую подписку пользователя. </summary>
    Task<UserSubscriptionDto?> GetUserSubscriptionAsync(string userId);

    /// <summary> Возвращает объединённую историю подписок пользователя. </summary>
    Task<IEnumerable<SubscriptionHistoryDto>> GetCombinedSubscriptionHistoryAsync(string userId);

    /// <summary> Проверяет наличие активной личной подписки. </summary>
    Task<bool> HasActiveSubscriptionAsync(string userId);

    /// <summary> Возвращает лимит транспортных средств пользователя. </summary>
    Task<int> GetUserVehicleLimitAsync(string userId);

    /// <summary> Возвращает следующий после текущего тариф. </summary>
    Task<SubscriptionPlanDto?> GetNextPlanAsync(string userId);

    /// <summary> Проверяет, является ли текущий тариф максимальным. </summary>
    Task<bool> IsMaxPlanAsync(string userId);

    /// <summary> Возвращает информацию о продлении личной подписки. </summary>
    Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(string userId);

    /// <summary> Проверяет возможность продления личной подписки. </summary>
    Task<bool> CanExtendSubscriptionAsync(string userId);

    /// <summary> Проверяет доступность демо-подписки для пользователя. </summary>
    Task<(bool Available, string Message)> CheckDemoSubscriptionAvailabilityAsync(string userId);
    /// <summary>
    /// Инициализирует системные тарифы и прогревает доступ к бесплатному тарифу без создания подписки.
    /// </summary>
    Task EnsureDefaultPlanAccessAsync(string userId);
}
