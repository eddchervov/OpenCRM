using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Управляет демо-подпиской и тестовым режимом рабочего пространства без оплаты.</summary>
public interface IOrganizationSubscriptionCommandService
{
    /// <summary> Активирует демо-подписку организации. </summary>
    Task<SubscriptionResult> ActivateDemoAsync(string userId, Guid organizationId, int planId, int? durationDays = null);

    /// <summary> Активирует тариф организации в тестовом режиме. </summary>
    Task<SubscriptionResult> ActivateForTestingAsync(string userId, Guid organizationId, int planId);

    /// <summary> Продлевает подписку организации в тестовом режиме. </summary>
    Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId, Guid organizationId);
}
