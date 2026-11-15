namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO подписки пользователя для админ-панели. </summary>
public sealed class AdminUserSubscriptionDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Признак демо-тарифа. </summary>
    public bool IsDemoPlan { get; init; }

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Признак автопродления подписки. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive { get; init; }

    /// <summary> Количество оставшихся дней подписки. </summary>
    public int DaysLeft { get; init; }
}
