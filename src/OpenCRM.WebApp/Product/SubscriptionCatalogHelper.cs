using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.WebApp.Product;

/// <summary> Вспомогательные методы фильтрации каталога тарифных планов. </summary>
public static class SubscriptionCatalogHelper
{
    /// <summary> Возвращает группы тарифов для частных пользователей. </summary>
    public static IReadOnlyList<SubscriptionPlanGroupDto> GetPersonalGroups(IEnumerable<SubscriptionPlanGroupDto> groups)
    {
        return FilterGroupsByType(groups, SubscriptionPlanCatalogCodes.PlanTypes.Personal);
    }

    /// <summary> Возвращает группы бизнес-тарифов. </summary>
    public static IReadOnlyList<SubscriptionPlanGroupDto> GetBusinessGroups(IEnumerable<SubscriptionPlanGroupDto> groups)
    {
        return FilterGroupsByType(groups, SubscriptionPlanCatalogCodes.PlanTypes.Business);
    }

    /// <summary> Определяет наличие бизнес-групп тарифов. </summary>
    public static bool HasBusinessGroups(IEnumerable<SubscriptionPlanGroupDto> groups)
    {
        return groups.Any(x => x.Plans.Any(plan =>
            SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) == SubscriptionPlanCatalogCodes.PlanTypes.Business));
    }

    /// <summary> Фильтрует группы тарифов по коду типа плана. </summary>
    private static IReadOnlyList<SubscriptionPlanGroupDto> FilterGroupsByType(IEnumerable<SubscriptionPlanGroupDto> groups, string planTypeCode)
    {
        return groups
            .Select(group => new SubscriptionPlanGroupDto
            {
                GroupName = group.GroupName == SubscriptionPlanCatalogCodes.OrganizationFreeGroup ? "Бесплатный" : group.GroupName,
                Order = group.Order,
                Plans = group.Plans
                    .Where(plan => SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) == planTypeCode)
                    .Where(plan => planTypeCode != SubscriptionPlanCatalogCodes.PlanTypes.Business
                        || !plan.IsDemoPlan && plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
                    .OrderByDescending(plan => plan.IsDemoPlan)
                    .ThenBy(plan => plan.Order)
                    .ThenBy(plan => plan.EffectivePrice)
                    .ToList()
            })
            .Where(group => group.Plans.Any())
            .OrderByDescending(group => group.Plans.Any(plan => plan.IsDemoPlan))
            .ThenBy(group => group.Order)
            .ThenBy(group => group.GroupName)
            .ToList();
    }
}
