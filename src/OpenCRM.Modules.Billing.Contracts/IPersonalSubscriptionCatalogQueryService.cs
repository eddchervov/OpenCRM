using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>
/// Каталог страницы личных подписок и выбор доступных личных тарифов.
/// </summary>
public interface IPersonalSubscriptionCatalogQueryService
{
    /// <summary>
    /// Возвращает личные и бизнес-тарифы с учётом доступности демо для пользователя.
    /// </summary>
    Task<IEnumerable<SubscriptionPlanDto>> GetCatalogPlansAsync(string? userId = null);

    /// <summary> Возвращает доступные личные тарифы. </summary>
    Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync(string? userId = null);

    /// <summary> Возвращает личные тарифы, сгруппированные для витрины. </summary>
    Task<IEnumerable<SubscriptionPlanGroupDto>> GetGroupedPlansAsync(string? userId = null);

    /// <summary> Возвращает тариф по умолчанию. </summary>
    Task<SubscriptionPlanDto?> GetDefaultPlanAsync();

    /// <summary> Возвращает демо-тариф. </summary>
    Task<SubscriptionPlanDto?> GetDemoPlanAsync();
}
