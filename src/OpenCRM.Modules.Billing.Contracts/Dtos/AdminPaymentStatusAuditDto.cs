namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Запись аудита изменения статуса платежа. </summary>
public sealed class AdminPaymentStatusAuditDto
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; init; }

    /// <summary> Предыдущий статус платежа. </summary>
    public string PreviousStatus { get; init; } = string.Empty;

    /// <summary> Новый статус платежа. </summary>
    public string NewStatus { get; init; } = string.Empty;

    /// <summary> Источник изменения статуса. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Причина изменения статуса. </summary>
    public string? Reason { get; init; }

    /// <summary> Идентификатор пользователя, изменившего статус. </summary>
    public string ChangedByUserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя изменившего статус. </summary>
    public string ChangedBy { get; init; } = string.Empty;

    /// <summary> Дата и время изменения статуса. </summary>
    public DateTime ChangedAt { get; init; }
}
