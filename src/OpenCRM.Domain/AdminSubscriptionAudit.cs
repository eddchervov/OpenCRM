namespace OpenCRM.Domain;

/// <summary> Запись аудита действий администратора по подписке. </summary>
public sealed class AdminSubscriptionAudit
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; set; }

    /// <summary> Область действия записи аудита. </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary> Идентификатор подписки. </summary>
    public int SubscriptionId { get; set; }

    /// <summary> Идентификатор клиента подписки. </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary> Выполненное действие над подпиской. </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary> Предыдущая дата окончания подписки. </summary>
    public DateTime? PreviousEndDate { get; set; }

    /// <summary> Новая дата окончания подписки. </summary>
    public DateTime? NewEndDate { get; set; }

    /// <summary> Идентификатор пользователя, изменившего подписку. </summary>
    public string ChangedByUserId { get; set; } = string.Empty;

    /// <summary> Дата и время изменения записи. </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary> Причина изменения подписки. </summary>
    public string Reason { get; set; } = string.Empty;
}
