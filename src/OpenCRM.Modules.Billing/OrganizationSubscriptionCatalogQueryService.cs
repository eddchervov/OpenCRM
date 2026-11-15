using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис каталога тарифов организаций. </summary>
internal sealed class OrganizationSubscriptionCatalogQueryService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationEntitlementService organizationEntitlementService,
    IPromoCodeService promoCodeService,
    OrganizationEntitlementReader reader) : IOrganizationSubscriptionCatalogQueryService
{
    /// <summary> Возвращает доступные тарифы для организации. </summary>
    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(
        string userId,
        Guid organizationId,
        bool includeUnavailableDemoForTesting = false)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();
        await reader.EnsureCanReadAsync(userId, organizationId);

        var canUseTestSubscriptions = await context.ApplicationUsers
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.CanUseTestSubscriptions)
            .FirstOrDefaultAsync();

        var demoAvailability = await organizationEntitlementService.CheckDemoAvailabilityAsync(userId, organizationId);

        var plans = await context.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.Features)
                .ThenInclude(feature => feature.FeatureDefinition)
            .Where(plan => plan.IsActive
                && !plan.IsDefaultPlan
                && plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business
                && ((includeUnavailableDemoForTesting && canUseTestSubscriptions)
                    || !plan.IsDemoPlan
                    || demoAvailability.Available))
            .OrderBy(plan => plan.Order)
            .ThenBy(plan => plan.DiscountPrice.HasValue && plan.DiscountPrice.Value < plan.Price
                ? plan.DiscountPrice.Value
                : plan.Price)
            .ToListAsync();

        var planDtos = plans.Select(SubscriptionEntitlementMapper.MapPlan).ToList();
        foreach (var planDto in planDtos.Where(plan => !plan.IsDemoPlan))
        {
            var promotion = await promoCodeService.CalculatePriceAsync(
                userId,
                organizationId,
                planDto.Id,
                planDto.EffectivePrice);
            planDto.OriginalEffectivePrice = promotion.OriginalPrice;
            planDto.EffectivePrice = promotion.FinalPrice;
            planDto.HasPromoDiscount = promotion.HasDiscount;
            planDto.PromoCode = promotion.PromoCode;
        }

        return planDtos;
    }
}
