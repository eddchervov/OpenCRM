using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис запросов каталога личных тарифов. </summary>
internal sealed class PersonalSubscriptionCatalogQueryService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    PersonalSubscriptionReader reader) : IPersonalSubscriptionCatalogQueryService
{
    /// <summary> Возвращает тарифы каталога личных подписок. </summary>
    public async Task<IEnumerable<SubscriptionPlanDto>> GetCatalogPlansAsync(string? userId = null)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var query = context.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.Features)
                .ThenInclude(feature => feature.FeatureDefinition)
            .Where(plan => plan.IsActive && !plan.IsDefaultPlan);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var demoAvailability = await reader.CheckDemoAvailabilityAsync(userId);
            var canUseTestSubscriptions = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.CanUseTestSubscriptions)
                .FirstOrDefaultAsync();

            if (!demoAvailability.Available && !canUseTestSubscriptions)
            {
                query = query.Where(plan => !plan.IsDemoPlan);
            }
        }
        else
        {
            query = query.Where(plan => !plan.IsDemoPlan);
        }

        var plans = await query
            .OrderBy(plan => plan.Order)
            .ThenBy(plan => plan.DiscountPrice.HasValue && plan.DiscountPrice.Value < plan.Price
                ? plan.DiscountPrice.Value
                : plan.Price)
            .ToListAsync();

        return plans.Select(PersonalSubscriptionMapper.MapPlan).ToList();
    }

    /// <summary> Возвращает доступные пользователю тарифы. </summary>
    public async Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync(string? userId = null)
    {
        var plans = await this.GetCatalogPlansAsync(userId);

        return plans
            .Where(plan => plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
            .ToList();
    }

    /// <summary> Возвращает тарифы, сгруппированные по группам. </summary>
    public async Task<IEnumerable<SubscriptionPlanGroupDto>> GetGroupedPlansAsync(string? userId = null)
    {
        var plans = await this.GetAvailablePlansAsync(userId);

        return plans
            .GroupBy(plan => string.IsNullOrEmpty(plan.Group) ? "Основные тарифы" : plan.Group)
            .Select(group => new SubscriptionPlanGroupDto
            {
                GroupName = group.Key,
                Plans = group.OrderBy(plan => plan.Order),
                Order = PersonalSubscriptionCatalogQueryService.GetGroupOrder(group.Key)
            })
            .OrderBy(group => group.Order)
            .ThenBy(group => group.GroupName)
            .ToList();
    }

    /// <summary> Возвращает тариф по умолчанию. </summary>
    public async Task<SubscriptionPlanDto?> GetDefaultPlanAsync()
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .Include(subscriptionPlan => subscriptionPlan.Features)
                .ThenInclude(feature => feature.FeatureDefinition)
            .FirstOrDefaultAsync(subscriptionPlan =>
                subscriptionPlan.IsActive
                && subscriptionPlan.IsDefaultPlan
                && subscriptionPlan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);

        return plan == null ? null : PersonalSubscriptionMapper.MapPlan(plan);
    }

    /// <summary> Возвращает демо-тариф. </summary>
    public async Task<SubscriptionPlanDto?> GetDemoPlanAsync()
    {
        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(subscriptionPlan =>
                subscriptionPlan.IsActive
                && subscriptionPlan.IsDemoPlan
                && subscriptionPlan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);

        return plan == null ? null : PersonalSubscriptionMapper.MapPlan(plan);
    }

    /// <summary> Возвращает порядок сортировки группы тарифов. </summary>
    private static int GetGroupOrder(string groupName)
    {
        return groupName switch
        {
            "Популярные тарифы" => 0,
            "Основные тарифы" => 1,
            _ => 2
        };
    }
}
