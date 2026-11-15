using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Активирует тариф рабочего пространства после подтверждения платежа провайдером.
/// </summary>
public interface IOrganizationSubscriptionActivationService
{
    /// <summary> Активирует новую подписку организации после оплаты. </summary>
    Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId);

    /// <summary> Активирует продление подписки организации после оплаты. </summary>
    Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, Guid organizationId, int planId, string paymentId);
}
