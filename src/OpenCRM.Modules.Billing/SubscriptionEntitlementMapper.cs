using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Маппер подписок организаций и функций в DTO. </summary>
internal static class SubscriptionEntitlementMapper
{
    /// <summary> Преобразует подписку организации в DTO. </summary>
    public static OrganizationSubscriptionDto MapSubscription(OrganizationSubscription subscription)
    {
        return new OrganizationSubscriptionDto
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            SubscriptionPlanId = subscription.SubscriptionPlanId,
            SubscriptionPlan = SubscriptionEntitlementMapper.MapPlan(subscription.SubscriptionPlan),
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            AutoRenewal = subscription.AutoRenewal,
            SourceCode = subscription.SourceCode,
            PromoDaysStartDate = subscription.PromoDaysStartDate
        };
    }

    /// <summary> Преобразует тариф организации в DTO. </summary>
    public static SubscriptionPlanDto MapPlan(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            OriginalEffectivePrice = plan.EffectivePrice,
            DiscountPrice = plan.DiscountPrice,
            EffectivePrice = plan.EffectivePrice,
            HasDiscount = plan.HasDiscount,
            DurationDays = plan.DurationDays,
            IsActive = plan.IsActive,
            VehicleLimit = plan.VehicleLimit,
            Order = plan.Order,
            Group = plan.Group,
            PlanTypeCode = plan.PlanTypeCode,
            PlanTypeName = SubscriptionPlanCatalogCodes.GetPlanTypeName(plan.PlanTypeCode),
            IsDemoPlan = plan.IsDemoPlan,
            IsDefaultPlan = plan.IsDefaultPlan,
            Features = plan.Features
                .OrderBy(feature => feature.FeatureDefinition.Name)
                .Select(SubscriptionEntitlementMapper.MapPlanFeatureValue)
                .ToList()
        };
    }

    /// <summary> Формирует значение функции из определения и привязки к тарифу. </summary>
    public static FeatureValueDto MapSnapshotFeatureValue(
        FeatureDefinition definition,
        SubscriptionPlanFeature? assignment,
        int defaultIntegerValue)
    {
        return new FeatureValueDto
        {
            FeatureId = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Category = definition.Category,
            ScopeCode = definition.ScopeCode,
            ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
            AudienceCode = definition.AudienceCode,
            AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
            ValueType = definition.ValueType.ToString(),
            BoolValue = definition.ValueType == FeatureValueType.Boolean ? assignment?.BoolValue ?? false : null,
            IntValue = definition.ValueType == FeatureValueType.Integer
                ? assignment?.IntValue ?? defaultIntegerValue
                : null
        };
    }

    /// <summary> Преобразует функцию тарифа в DTO. </summary>
    private static FeatureValueDto MapPlanFeatureValue(SubscriptionPlanFeature feature)
    {
        var definition = feature.FeatureDefinition;
        return new FeatureValueDto
        {
            FeatureId = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Category = definition.Category,
            ScopeCode = definition.ScopeCode,
            ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
            AudienceCode = definition.AudienceCode,
            AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
            ValueType = definition.ValueType.ToString(),
            BoolValue = feature.BoolValue,
            IntValue = feature.IntValue
        };
    }
}
