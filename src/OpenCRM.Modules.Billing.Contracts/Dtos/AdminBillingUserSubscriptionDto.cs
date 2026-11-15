namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Подписка пользователя в административном отчёте. </summary>
public sealed class AdminBillingUserSubscriptionDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
    public string User { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? UserEmail { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? UserPhone { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Признак демо-тарифа. </summary>
    public bool IsDemoPlan { get; init; }

    /// <summary> Признак архивной записи подписки. </summary>
    public bool IsHistorical { get; init; }

    /// <summary> Источник подписки. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Признак автопродления подписки. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive { get; init; }

    /// <summary> Признак скорого окончания подписки. </summary>
    public bool IsExpiringSoon { get; init; }

    /// <summary> Оставшееся количество дней подписки. </summary>
    public int DaysLeft { get; init; }
}

