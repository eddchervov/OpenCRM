namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos;

/// <summary> Личная подписка пользователя. </summary>
public class UserSubscriptionDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }    

    /// <summary> Тарифный план подписки. </summary>
    public SubscriptionPlanDto SubscriptionPlan { get; set; } = null!; 

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; set; } 

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; set; }

    /// <summary> Признак автопродления подписки. </summary>
    public bool AutoRenewal { get; set; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive => EndDate >= DateTime.UtcNow;
}

