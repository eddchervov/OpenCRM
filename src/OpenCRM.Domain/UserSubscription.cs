using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Подписка пользователя на тарифный план. </summary>
public class UserSubscription
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; set; }

    /// <summary> Покупка, предоставившая текущий снимок условий доступа. </summary>
    public Guid? SubscriptionPurchaseId { get; set; }

    /// <summary> Неизменяемый снимок оплаченных условий. </summary>
    public SubscriptionPurchase? SubscriptionPurchase { get; set; }

    /// <summary> Идентификатор пользователя — владельца подписки. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь — владелец подписки. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана подписки. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план подписки. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; set; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; set; }

    /// <summary> Признак автоматического продления подписки. </summary>
    public bool AutoRenewal { get; set; }

    /// <summary> Код источника оформления подписки. </summary>
    public string SourceCode { get; set; } = "unknown";
}
