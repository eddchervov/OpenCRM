namespace OpenCRM.Domain;

/// <summary> Возможность, входящая в тарифный план подписки. </summary>
public class SubscriptionPlanFeature
{
    /// <summary> Идентификатор возможности тарифного плана. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор тарифного плана, к которому относится возможность. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план, к которому относится возможность. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Идентификатор определения возможности. </summary>
    public int FeatureDefinitionId { get; set; }

    /// <summary> Определение возможности тарифного плана. </summary>
    public FeatureDefinition FeatureDefinition { get; set; } = null!;

    /// <summary> Логическое значение возможности тарифного плана. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение возможности тарифного плана. </summary>
    public int? IntValue { get; set; }
}

