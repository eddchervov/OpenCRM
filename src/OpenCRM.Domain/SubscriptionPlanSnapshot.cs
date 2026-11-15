using System.Text.Json;

namespace OpenCRM.Domain;

/// <summary> Неизменяемый снимок оплаченных условий тарифа, включая значения возможностей. </summary>
public sealed record SubscriptionPlanSnapshot(
    int Id,
    string Name,
    decimal Price,
    int DurationDays,
    int VehicleLimit,
    int Order,
    string? Group,
    string PlanTypeCode,
    List<SubscriptionFeatureSnapshot> Features)
{
    public static string Capture(SubscriptionPlan plan) => JsonSerializer.Serialize(new SubscriptionPlanSnapshot(
        plan.Id, plan.Name, plan.EffectivePrice, plan.DurationDays, plan.VehicleLimit, plan.Order, plan.Group,
        plan.PlanTypeCode, plan.Features.Select(feature => new SubscriptionFeatureSnapshot(
            feature.FeatureDefinitionId, feature.FeatureDefinition.Code, feature.FeatureDefinition.Name,
            feature.FeatureDefinition.Category, feature.FeatureDefinition.ScopeCode,
            feature.FeatureDefinition.AudienceCode, feature.FeatureDefinition.ValueType,
            feature.BoolValue, feature.IntValue)).ToList()));

    public static SubscriptionPlan Restore(string value)
    {
        var snapshot = JsonSerializer.Deserialize<SubscriptionPlanSnapshot>(value)
            ?? throw new InvalidOperationException("Снимок оплаченного тарифа не найден.");
        if (snapshot.Id <= 0 || snapshot.DurationDays <= 0 || snapshot.Features == null)
        {
            throw new InvalidOperationException("Снимок оплаченного тарифа повреждён.");
        }

        return new SubscriptionPlan
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Price = snapshot.Price,
            DurationDays = snapshot.DurationDays,
            VehicleLimit = snapshot.VehicleLimit,
            Order = snapshot.Order,
            Group = snapshot.Group,
            PlanTypeCode = snapshot.PlanTypeCode,
            Features = snapshot.Features.Select(feature => new SubscriptionPlanFeature
            {
                SubscriptionPlanId = snapshot.Id,
                FeatureDefinitionId = feature.Id,
                BoolValue = feature.BoolValue,
                IntValue = feature.IntValue,
                FeatureDefinition = new FeatureDefinition
                {
                    Id = feature.Id,
                    Code = feature.Code,
                    Name = feature.Name,
                    Category = feature.Category,
                    ScopeCode = feature.ScopeCode,
                    AudienceCode = feature.AudienceCode,
                    ValueType = feature.ValueType,
                    IsActive = true
                }
            }).ToList()
        };
    }
}

/// <summary> Снимок значения возможности без изменяемой связи со справочником. </summary>
public sealed record SubscriptionFeatureSnapshot(
    int Id,
    string Code,
    string Name,
    string? Category,
    string ScopeCode,
    string AudienceCode,
    FeatureValueType ValueType,
    bool? BoolValue,
    int? IntValue);
