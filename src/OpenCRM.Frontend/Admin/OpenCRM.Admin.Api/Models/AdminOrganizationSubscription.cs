namespace OpenCRM.Admin.Api.Models;

/// <summary> Подписка организации для административного просмотра. </summary>
public sealed class AdminOrganizationSubscription
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Признак демо-тарифного плана. </summary>
    public bool IsDemoPlan { get; init; }

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Признак автоматического продления. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive { get; init; }

    /// <summary> Количество дней до окончания подписки. </summary>
    public int DaysLeft { get; init; }
}
