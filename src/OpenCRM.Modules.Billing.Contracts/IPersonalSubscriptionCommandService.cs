using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Выполняет управляющие команды для личных подписок.
/// </summary>
public interface IPersonalSubscriptionCommandService
{
    /// <summary> Активирует личную подписку в тестовом режиме. </summary>
    Task<SubscriptionResult> ActivateSubscriptionForTestingAsync(string userId, int planId);

    /// <summary> Продлевает личную подписку в тестовом режиме. </summary>
    Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId);

    /// <summary> Отменяет личную подписку в тестовом режиме. </summary>
    Task<SubscriptionResult> CancelSubscriptionForTestingAsync(string userId);

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    Task<SubscriptionResult> GrantDemoSubscriptionAsync(string userId);

    /// <summary> Выдаёт пользователю демо-подписку администратором. </summary>
    Task<SubscriptionResult> GrantDemoSubscriptionByAdminAsync(string userId);

    /// <summary> Назначает пользователю тариф администратором. </summary>
    Task<SubscriptionResult> AssignSubscriptionByAdminAsync(string userId, int planId, string adminName);

    /// <summary> Возвращает пользователя на тариф по умолчанию администратором. </summary>
    Task<SubscriptionResult> ReturnUserToDefaultPlanByAdminAsync(string userId, string adminName);

    /// <summary> Проверяет и деактивирует истёкшие личные подписки. </summary>
    Task CheckAndDeactivateExpiredSubscriptionsAsync();
}
