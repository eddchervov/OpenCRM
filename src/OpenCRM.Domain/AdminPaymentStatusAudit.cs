namespace OpenCRM.Domain;

/// <summary> Запись аудита ручного или автоматического изменения статуса платежа. </summary>
public sealed class AdminPaymentStatusAudit
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор платежа. </summary>
    public int PaymentId { get; set; }

    /// <summary> Платёж по подписке, к которому относится запись аудита. </summary>
    public SubscriptionPayment Payment { get; set; } = null!;

    /// <summary> Предыдущий статус платежа. </summary>
    public string PreviousStatus { get; set; } = string.Empty;

    /// <summary> Новый статус платежа. </summary>
    public string NewStatus { get; set; } = string.Empty;

    /// <summary> Источник изменения статуса платежа. </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary> Причина изменения статуса платежа. </summary>
    public string? Reason { get; set; }

    /// <summary> Идентификатор пользователя или системного процесса, изменившего статус. </summary>
    public string ChangedByUserId { get; set; } = string.Empty;

    /// <summary> Дата и время изменения записи. </summary>
    public DateTime ChangedAt { get; set; }
}
