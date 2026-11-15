using OpenCRM.Domain;
using OpenCRM.Modules.Billing;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class SubscriptionEntitlementMapperTests
{
    [Fact]
    public void MapsSubscriptionAndNestedPlanValues()
    {
        var definition = new FeatureDefinition
        {
            Id = 1,
            Code = "billing.test.enabled",
            Name = "Доступ",
            Category = "Модули",
            ScopeCode = FeatureCatalogCodes.Scopes.Organization,
            AudienceCode = FeatureCatalogCodes.Audiences.Admin,
            ValueType = FeatureValueType.Boolean
        };
        var plan = new SubscriptionPlan
        {
            Id = 3,
            Name = "План",
            Description = "Описание",
            Price = 100,
            DiscountPrice = 75,
            DurationDays = 30,
            VehicleLimit = 12,
            Order = 4,
            Group = "Основной",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsDemoPlan = true,
            Features = [new SubscriptionPlanFeature { FeatureDefinition = definition, BoolValue = true }]
        };
        var subscription = new OrganizationSubscription
        {
            Id = 5,
            OrganizationId = Guid.NewGuid(),
            SubscriptionPlanId = plan.Id,
            SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(29),
            AutoRenewal = true
        };

        var mapped = SubscriptionEntitlementMapper.MapSubscription(subscription);

        Assert.Equal(subscription.Id, mapped.Id);
        Assert.Equal(subscription.OrganizationId, mapped.OrganizationId);
        Assert.Equal(subscription.SubscriptionPlanId, mapped.SubscriptionPlanId);
        Assert.Equal(subscription.StartDate, mapped.StartDate);
        Assert.Equal(subscription.EndDate, mapped.EndDate);
        Assert.True(mapped.AutoRenewal);
        Assert.Equal("План", mapped.SubscriptionPlan.Name);
        Assert.Equal(75, mapped.SubscriptionPlan.EffectivePrice);
        Assert.True(mapped.SubscriptionPlan.HasDiscount);
        Assert.True(mapped.SubscriptionPlan.IsDemoPlan);
        Assert.Equal("Бизнес", mapped.SubscriptionPlan.PlanTypeName);
        Assert.Equal("Админская", Assert.Single(mapped.SubscriptionPlan.Features).AudienceName);
    }

    [Fact]
    public void MapsIntegerFeatureWithNullAssignmentAndFallbackDefault()
    {
        var definition = new FeatureDefinition
        {
            Id = 2,
            Code = "billing.test.limit",
            Name = "Лимит",
            ScopeCode = FeatureCatalogCodes.Scopes.Both,
            AudienceCode = FeatureCatalogCodes.Audiences.User,
            ValueType = FeatureValueType.Integer
        };

        var mapped = SubscriptionEntitlementMapper.MapSnapshotFeatureValue(definition, null, 7);

        Assert.Equal(7, mapped.IntValue);
        Assert.Null(mapped.BoolValue);
        Assert.Equal("Личный и workspace", mapped.ScopeName);
    }

    [Fact]
    public void MapsFeatureWithAssignedIntegerAndBooleanFalseWithoutDiscount()
    {
        var integerDefinition = new FeatureDefinition
        {
            Code = "billing.test.limit",
            Name = "Лимит",
            ScopeCode = "invalid",
            AudienceCode = "invalid",
            ValueType = FeatureValueType.Integer
        };
        var booleanDefinition = new FeatureDefinition
        {
            Code = "billing.test.flag",
            Name = "Флаг",
            ValueType = FeatureValueType.Boolean
        };
        var plan = new SubscriptionPlan
        {
            Name = "Без скидки",
            Price = 120,
            DurationDays = 1,
            PlanTypeCode = "unknown",
            Features =
            [
                new SubscriptionPlanFeature
                {
                    FeatureDefinition = integerDefinition,
                    IntValue = 9
                },
                new SubscriptionPlanFeature
                {
                    FeatureDefinition = booleanDefinition,
                    BoolValue = false
                }
            ]
        };

        var mapped = SubscriptionEntitlementMapper.MapPlan(plan);

        Assert.False(mapped.HasDiscount);
        Assert.Equal(120, mapped.EffectivePrice);
        Assert.Equal("Личный", mapped.PlanTypeName);
        Assert.Equal(new[] { "Лимит", "Флаг" }, mapped.Features.Select(feature => feature.Name));
        Assert.Equal(9, mapped.Features.Single(feature => feature.Name == "Лимит").IntValue);
        Assert.False(mapped.Features.Single(feature => feature.Name == "Флаг").BoolValue);
        Assert.Equal("Личный и workspace", mapped.Features.Single(feature => feature.Name == "Лимит").ScopeName);
        Assert.Equal("Обычная", mapped.Features.Single(feature => feature.Name == "Лимит").AudienceName);
    }
}
