namespace OpenCRM.Admin.Api.Models;

/// <summary> Подписка пользователя для административного просмотра. </summary>
public sealed class AdminBillingUserSubscription
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя пользователя. </summary>
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

    /// <summary> Признак демо-тарифного плана. </summary>
    public bool IsDemoPlan { get; init; }

    /// <summary> Признак исторической подписки. </summary>
    public bool IsHistorical { get; init; }

    /// <summary> Источник данных о подписке. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Признак автоматического продления. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Признак активной подписки. </summary>
    public bool IsActive { get; init; }

    /// <summary> Признак скорого истечения подписки. </summary>
    public bool IsExpiringSoon { get; init; }

    /// <summary> Количество дней до окончания подписки. </summary>
    public int DaysLeft { get; init; }
}
