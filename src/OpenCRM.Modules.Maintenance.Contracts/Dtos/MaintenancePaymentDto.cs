namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные платежа по обслуживанию. </summary>
public sealed class MaintenancePaymentDto
{
    /// <summary> Идентификатор клиента-плательщика, если указан. </summary>
    public Guid? PayerClientId { get; set; }

    /// <summary> Наименование плательщика. </summary>
    public string? PayerName { get; set; }

    /// <summary> Идентификатор платежа. </summary>
    public Guid Id { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Код способа оплаты. </summary>
    public string? MethodCode { get; init; }

    /// <summary> Дата создания платежа. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Пользователь, создавший платеж. </summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary> Комментарий к платежу. </summary>
    public string? Comment { get; init; }

    /// <summary> Признак платежа начального остатка. </summary>
    public bool IsOpeningBalance { get; init; }

    /// <summary> Дата отмены платежа, если отменён. </summary>
    public DateTime? CancelledAt { get; init; }

    /// <summary> Пользователь, отменивший платеж. </summary>
    public string? CancelledBy { get; init; }

    /// <summary> Причина отмены платежа. </summary>
    public string? CancellationReason { get; init; }
}
