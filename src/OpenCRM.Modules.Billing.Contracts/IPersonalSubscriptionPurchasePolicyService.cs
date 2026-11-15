using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Проверяет правила покупки личного тарифа.
/// </summary>
public interface IPersonalSubscriptionPurchasePolicyService
{
    /// <summary> Проверяет правила покупки личного тарифа. </summary>
    Task<SubscriptionResult> CanPurchasePlanAsync(string userId, int planId);
}
