using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Читает тарифные возможности организации без покупки и активации подписки.</summary>
public interface IOrganizationEntitlementService
{
    /// <summary> Возвращает текущую подписку организации. </summary>
    Task<OrganizationSubscriptionDto?> GetCurrentAsync(string userId, Guid organizationId);

    /// <summary> Возвращает действующий тариф организации. </summary>
    Task<SubscriptionPlanDto?> GetEffectivePlanAsync(string userId, Guid organizationId);

    /// <summary> Проверяет наличие активной подписки организации. </summary>
    Task<bool> HasActiveSubscriptionAsync(string userId, Guid organizationId);

    /// <summary> Возвращает лимит транспортных средств организации. </summary>
    Task<int> GetVehicleLimitAsync(string userId, Guid organizationId, int fallbackLimit);

    /// <summary> Возвращает лимит участников организации. </summary>
    Task<int> GetMembersLimitAsync(string userId, Guid organizationId, int fallbackLimit);

    /// <summary> Проверяет доступность функции для организации. </summary>
    Task<bool> HasFeatureAsync(string userId, Guid organizationId, string featureCode);

    /// <summary> Возвращает снимок функций организации. </summary>
    Task<FeatureAccessSnapshotDto> GetFeatureSnapshotAsync(string userId, Guid organizationId);

    /// <summary> Проверяет доступность демо-тарифа для организации. </summary>
    Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(string userId, Guid organizationId);

    /// <summary> Возвращает тарифные возможности рабочего пространства организации. </summary>
    Task<OrganizationWorkspaceEntitlementDto> GetWorkspaceEntitlementAsync(string userId, Guid organizationId);
}
