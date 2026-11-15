using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса подписок организации. </summary>
public interface IOrganizationSubscriptionService
{
    /// <summary> Возвращает текущую подписку организации. </summary>
    Task<OrganizationSubscriptionDto?> GetCurrentAsync(string userId, Guid organizationId);

    /// <summary> Возвращает действующий тарифный план организации. </summary>
    Task<SubscriptionPlanDto?> GetEffectivePlanAsync(string userId, Guid organizationId);

    /// <summary> Возвращает доступные тарифные планы организации. </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(string userId, Guid organizationId, bool includeUnavailableDemoForTesting = false);

    /// <summary> Проверяет возможность покупки тарифного плана. </summary>
    Task<SubscriptionResult> CanPurchasePlanAsync(string userId, Guid organizationId, int planId);

    /// <summary> Оформляет покупку подписки организации на тарифный план. </summary>
    Task<SubscriptionResult> PurchaseSubscriptionAsync(string userId, Guid organizationId, int planId, string? paymentMethodId = null, PaymentBuyerDto? buyer = null);

    /// <summary> Проверяет возможность продления подписки организации. </summary>
    Task<bool> CanExtendSubscriptionAsync(string userId, Guid organizationId);

    /// <summary> Возвращает информацию о возможности продления подписки. </summary>
    Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(string userId, Guid organizationId);

    /// <summary> Продлевает подписку организации с оплатой. </summary>
    Task<SubscriptionResult> ExtendSubscriptionAsync(string userId, Guid organizationId, PaymentBuyerDto? buyer = null);

    /// <summary> Активирует новую подписку после оплаты. </summary>
    Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId);

    /// <summary> Активирует продлённую подписку после оплаты. </summary>
    Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId);

    /// <summary> Возвращает права доступа пользователя к рабочему пространству. </summary>
    Task<OrganizationWorkspaceAccessDto> GetWorkspaceAccessAsync(string userId, Guid organizationId);

    /// <summary> Проверяет право чтения в рабочем пространстве, иначе сообщает об ошибке. </summary>
    Task EnsureWorkspaceCanReadAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary> Проверяет право записи в рабочем пространстве, иначе сообщает об ошибке. </summary>
    Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId);

    /// <summary> Проверяет указанное право записи в рабочем пространстве. </summary>
    Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary> Проверяет наличие активной подписки у организации. </summary>
    Task<bool> HasActiveSubscriptionAsync(string userId, Guid organizationId);

    /// <summary> Возвращает лимит транспортных средств по подписке. </summary>
    Task<int> GetVehicleLimitAsync(string userId, Guid organizationId, int fallbackLimit);

    /// <summary> Возвращает лимит участников по подписке. </summary>
    Task<int> GetMembersLimitAsync(string userId, Guid organizationId, int fallbackLimit);

    /// <summary> Проверяет доступность функции по её коду. </summary>
    Task<bool> HasFeatureAsync(string userId, Guid organizationId, string featureCode);

    /// <summary> Возвращает сводку доступности функций тарифа. </summary>
    Task<FeatureAccessSnapshotDto> GetFeatureSnapshotAsync(string userId, Guid organizationId);

    /// <summary> Проверяет доступность демо-подписки. </summary>
    Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(string userId, Guid organizationId);

    /// <summary> Активирует демо-подписку организации. </summary>
    Task<SubscriptionResult> ActivateDemoAsync(string userId, Guid organizationId, int planId, int? durationDays = null);

    /// <summary> Активирует подписку организации в тестовых целях. </summary>
    Task<SubscriptionResult> ActivateForTestingAsync(string userId, Guid organizationId, int planId);

    /// <summary> Продлевает подписку организации в тестовых целях. </summary>
    Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId, Guid organizationId);
}
