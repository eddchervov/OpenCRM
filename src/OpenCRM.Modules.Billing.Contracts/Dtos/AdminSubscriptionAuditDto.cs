namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Запись аудита изменения подписки. </summary>
public sealed class AdminSubscriptionAuditDto
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; init; }

    /// <summary> Идентификатор подписки. </summary>
    public int SubscriptionId { get; init; }

    /// <summary> Выполненное действие над подпиской. </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary> Предыдущая дата окончания подписки. </summary>
    public DateTime? PreviousEndDate { get; init; }

    /// <summary> Новая дата окончания подписки. </summary>
    public DateTime? NewEndDate { get; init; }

    /// <summary> Отображаемое имя изменившего подписку. </summary>
    public string ChangedBy { get; init; } = string.Empty;

    /// <summary> Дата и время изменения подписки. </summary>
    public DateTime ChangedAt { get; init; }

    /// <summary> Причина изменения подписки. </summary>
    public string Reason { get; init; } = string.Empty;
}
