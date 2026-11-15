namespace OpenCRM.Domain;

/// <summary> Подписка организации на тарифный план. </summary>
public class OrganizationSubscription
{
    /// <summary> Идентификатор подписки организации. </summary>
    public int Id { get; set; }

    /// <summary> Покупка, предоставившая текущий снимок условий доступа. </summary>
    public Guid? SubscriptionPurchaseId { get; set; }

    /// <summary> Неизменяемый снимок оплаченных условий. </summary>
    public SubscriptionPurchase? SubscriptionPurchase { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, владеющая подпиской. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план подписки. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Дата начала действия подписки. </summary>
    public DateTime StartDate { get; set; }

    /// <summary> Дата окончания действия подписки. </summary>
    public DateTime EndDate { get; set; }

    /// <summary> Признак автопродления подписки. </summary>
    public bool AutoRenewal { get; set; }

    /// <summary> Код источника создания подписки. </summary>
    public string SourceCode { get; set; } = "unknown";

    // Начало именно подарочных дней внутри promo-периода: доступ к features — от StartDate,
    // остаток demo может находиться между StartDate и PromoDaysStartDate.
    /// <summary> Дата начала промо-периода подписки. </summary>
    public DateTime? PromoDaysStartDate { get; set; }

    /// <summary> Признак активности подписки. </summary>
    public bool IsActive => EndDate >= DateTime.UtcNow;
}
