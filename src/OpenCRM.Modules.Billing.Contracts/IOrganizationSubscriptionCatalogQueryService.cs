using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Читает доступные бизнес-тарифы с учётом демо и промокода пользователя.</summary>
public interface IOrganizationSubscriptionCatalogQueryService
{
    /// <summary> Возвращает доступные организации тарифы с учётом демо и промокода. </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(
        string userId,
        Guid organizationId,
        bool includeUnavailableDemoForTesting = false);
}
