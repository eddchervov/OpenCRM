using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Проверяет правила покупки тарифа рабочего пространства.
/// </summary>
public interface IOrganizationSubscriptionPurchasePolicyService
{
    /// <summary> Проверяет правила покупки тарифа организации. </summary>
    Task<SubscriptionResult> CanPurchasePlanAsync(string userId, Guid organizationId, int planId);
}
