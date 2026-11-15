namespace OpenCRM.Domain;

/// <summary> Запись аудита действий администратора по чеку самозанятого. </summary>
public sealed class AdminNpdReceiptAudit
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор платежа по подписке. </summary>
    public int SubscriptionPaymentId { get; set; }

    /// <summary> Признак выдачи чека. </summary>
    public bool Issued { get; set; }

    /// <summary> Дата и время изменения записи. </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary> Идентификатор пользователя, изменившего запись. </summary>
    public string ChangedByUserId { get; set; } = string.Empty;

    /// <summary> Причина изменения записи аудита. </summary>
    public string Reason { get; set; } = string.Empty;
}
