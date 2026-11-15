using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Подписка организации. </summary>
public class OrganizationSubscriptionDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

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

    /// <summary> Код источника подписки. </summary>
    public string SourceCode { get; set; } = "unknown";

    /// <summary> Дата начала промо-периода подписки. </summary>
    public DateTime? PromoDaysStartDate { get; set; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive => EndDate >= DateTime.UtcNow;
}
