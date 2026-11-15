using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using System.Collections.Generic;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Services.Interfaces.Subscriptions;

/// <summary>
/// Сервис для управления подписками пользователей
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Получить текущую активную подписку пользователя
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>DTO текущей подписки или null если подписки нет</returns>
    Task<UserSubscriptionDto?> GetUserSubscriptionAsync(string userId);

    /// <summary>
    /// Получить список доступных тарифных планов
    /// </summary>
    /// <returns>Коллекция DTO тарифных планов</returns>
    Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync(string? userId = null);

    /// <summary> Возвращает тарифные планы каталога. </summary>
    Task<IEnumerable<SubscriptionPlanDto>> GetCatalogPlansAsync(string? userId = null);

    /// <summary>
    /// Получить базовый тариф по умолчанию
    /// </summary>
    Task<SubscriptionPlanDto?> GetDefaultPlanAsync();

    /// <summary>
    /// Убедиться, что пользователь получает базовый тариф как системный fallback,
    /// если у него нет активной подписки.
    /// </summary>
    Task EnsureDefaultPlanAccessAsync(string userId);

    /// <summary>
    /// Получить сгруппированные тарифные планы
    /// </summary>
    /// <returns>Коллекция сгруппированных тарифных планов</returns>
    Task<IEnumerable<SubscriptionPlanGroupDto>> GetGroupedPlansAsync(string? userId = null);

    /// <summary>
    /// Получить следующий доступный тарифный план для пользователя
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>DTO следующего тарифного плана или null если текущий максимальный</returns>
    Task<SubscriptionPlanDto?> GetNextPlanAsync(string userId);

    /// <summary>
    /// Проверить является ли текущий тариф пользователя максимальным
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>True если тариф максимальный, иначе False</returns>
    Task<bool> IsMaxPlanAsync(string userId);

    /// <summary>
    /// Проверить есть ли у пользователя активная подписка
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>True если есть активная подписка, иначе False</returns>
    Task<bool> HasActiveSubscriptionAsync(string userId);

    /// <summary>
    /// Оформить подписку на тарифный план
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <param name="planId">Идентификатор тарифного плана</param>
    /// <param name="paymentMethodId">Идентификатор метода оплаты (опционально)</param>
    /// <returns>Результат операции оформления подписки</returns>
    Task<SubscriptionResult> PurchaseSubscriptionAsync(string userId, int planId, string? paymentMethodId, string? returnUrl = null, PaymentBuyerDto? buyer = null);

    /// <summary>
    /// Продлить текущую активную подписку пользователя
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>Результат операции продления подписки</returns>
    Task<SubscriptionResult> ExtendSubscriptionAsync(string userId, string? returnUrl = null, PaymentBuyerDto? buyer = null);

    /// <summary>
    /// Проверить возможность покупки тарифного плана пользователем
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <param name="planId">Идентификатор тарифного плана</param>
    /// <returns>Результат проверки возможности покупки</returns>
    Task<SubscriptionResult> CanPurchasePlanAsync(string userId, int planId);

    /// <summary>
    /// Проверить может ли пользователь добавить новую технику
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>True если можно добавить технику, иначе False</returns>
    Task<bool> CanUserAddVehicleAsync(string userId);

    /// <summary>
    /// Получить текущий лимит техники для пользователя
    /// </summary>
    /// <param name="userId">Идентификатор пользователя</param>
    /// <returns>Лимит техники</returns>
    Task<int> GetUserVehicleLimitAsync(string userId);

    /// <summary>
    /// Проверить и деактивировать просроченные подписки
    /// </summary>
    Task CheckAndDeactivateExpiredSubscriptionsAsync();

    /// <summary> Возвращает информацию о возможности продления подписки. </summary>
    Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(string userId);

    /// <summary> Проверяет возможность продления подписки пользователя. </summary>
    Task<bool> CanExtendSubscriptionAsync(string userId);

    /// <summary>
    /// Получить объединенную историю подписок и платежей
    /// </summary>
    Task<IEnumerable<SubscriptionHistoryDto>> GetCombinedSubscriptionHistoryAsync(string userId);

    /// <summary> Проверяет доступность демо-подписки пользователя. </summary>
    Task<(bool Available, string Message)> CheckDemoSubscriptionAvailabilityAsync(string userId);

    /// <summary> Предоставляет демо-подписку пользователю. </summary>
    Task<SubscriptionResult> GrantDemoSubscriptionAsync(string userId);

    /// <summary> Предоставляет демо-подписку пользователю от имени администратора. </summary>
    Task<SubscriptionResult> GrantDemoSubscriptionByAdminAsync(string userId);

    /// <summary> Назначает подписку пользователю от имени администратора. </summary>
    Task<SubscriptionResult> AssignSubscriptionByAdminAsync(string userId, int planId, string adminName);

    /// <summary> Возвращает пользователя на тариф по умолчанию от имени администратора. </summary>
    Task<SubscriptionResult> ReturnUserToDefaultPlanByAdminAsync(string userId, string adminName);

    /// <summary> Активирует продлённую подписку после оплаты. </summary>
    Task<SubscriptionResult> ActivateExtendedSubscription(string userId, int planId, string paymentId);

    /// <summary> Активирует новую подписку после оплаты. </summary>
    Task<SubscriptionResult> ActivateNewSubscription(string userId, int planId, string paymentId);

    /// <summary> Активирует подписку пользователя в тестовых целях. </summary>
    Task<SubscriptionResult> ActivateSubscriptionForTestingAsync(string userId, int planId);

    /// <summary> Продлевает подписку пользователя в тестовых целях. </summary>
    Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId);

    /// <summary> Отменяет подписку пользователя в тестовых целях. </summary>
    Task<SubscriptionResult> CancelSubscriptionForTestingAsync(string userId);
}

