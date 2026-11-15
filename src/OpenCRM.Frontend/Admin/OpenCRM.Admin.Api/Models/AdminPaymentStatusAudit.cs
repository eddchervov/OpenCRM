namespace OpenCRM.Admin.Api.Models;

/// <summary> Аудит изменения статуса платежа. </summary>
public sealed class AdminPaymentStatusAudit
{
    /// <summary> Идентификатор записи аудита. </summary>
    public long Id { get; init; }

    /// <summary> Прежний статус платежа. </summary>
    public string PreviousStatus { get; init; } = string.Empty;

    /// <summary> Новый статус платежа. </summary>
    public string NewStatus { get; init; } = string.Empty;

    /// <summary> Источник изменения статуса. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Причина изменения статуса. </summary>
    public string? Reason { get; init; }

    /// <summary> Идентификатор пользователя, изменившего статус. </summary>
    public string ChangedByUserId { get; init; } = string.Empty;

    /// <summary> Имя пользователя, изменившего статус. </summary>
    public string ChangedBy { get; init; } = string.Empty;

    /// <summary> Момент изменения статуса. </summary>
    public DateTime ChangedAt { get; init; }
}
