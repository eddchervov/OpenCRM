namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO записи истории подписки пользователя. </summary>
public sealed class AdminUserSubscriptionHistoryDto
{
    /// <summary> Идентификатор подписки. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Дата начала подписки. </summary>
    public DateTime StartDate { get; init; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime EndDate { get; init; }

    /// <summary> Фактическая дата окончания подписки. </summary>
    public DateTime? ActualEndDate { get; init; }

    /// <summary> Статус подписки. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Признак автопродления подписки. </summary>
    public bool AutoRenewal { get; init; }

    /// <summary> Дата создания записи подписки. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Комментарий к подписке. </summary>
    public string? Comment { get; init; }
}
