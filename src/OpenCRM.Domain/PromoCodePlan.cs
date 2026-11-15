namespace OpenCRM.Domain;

/// <summary> Связь промокода с тарифным планом. </summary>
public class PromoCodePlan
{
    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Промокод, связанный с тарифным планом. </summary>
    public PromoCode PromoCode { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план, к которому применим промокод. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
}

