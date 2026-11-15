namespace OpenCRM.Admin.Api.Models;

/// <summary> Аудит изменения подписки. </summary>
public sealed class AdminSubscriptionAudit
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; init; }

    /// <summary> Идентификатор подписки. </summary>
    public int SubscriptionId { get; init; }

    /// <summary> Действие над подпиской. </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary> Прежняя дата окончания подписки. </summary>
    public DateTime? PreviousEndDate { get; init; }

    /// <summary> Новая дата окончания подписки. </summary>
    public DateTime? NewEndDate { get; init; }

    /// <summary> Имя пользователя, изменившего подписку. </summary>
    public string ChangedBy { get; init; } = string.Empty;

    /// <summary> Момент изменения. </summary>
    public DateTime ChangedAt { get; init; }

    /// <summary> Причина изменения. </summary>
    public string Reason { get; init; } = string.Empty;
}
