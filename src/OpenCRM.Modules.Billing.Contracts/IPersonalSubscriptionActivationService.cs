using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Активирует личные подписки после подтверждения платежа провайдером.
/// </summary>
public interface IPersonalSubscriptionActivationService
{
    /// <summary> Активирует новую личную подписку после оплаты. </summary>
    Task<SubscriptionResult> ActivateNewSubscriptionAsync(string userId, int planId, string paymentId);

    /// <summary> Активирует продление личной подписки после оплаты. </summary>
    Task<SubscriptionResult> ActivateExtendedSubscriptionAsync(string userId, int planId, string paymentId);
}
