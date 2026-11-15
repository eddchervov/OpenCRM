namespace OpenCRM.Admin.Api.Models;

/// <summary> Запись истории подписок пользователя. </summary>
public sealed class AdminUserSubscriptionHistory
{
    /// <summary> Идентификатор записи истории. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Фактическая дата окончания. </summary>
    public DateTime? ActualEndDate { get; init; }

    /// <summary> Статус подписки. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Признак автопродления. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Время создания записи. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Комментарий к записи. </summary>
    public string? Comment { get; init; }
}
