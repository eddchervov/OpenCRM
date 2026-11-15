namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Результат административной операции с подпиской. </summary>
public sealed class AdminBillingSubscriptionOperationDto
{
    /// <summary> Признак успешного выполнения операции. </summary>
    public bool Succeeded { get; init; }

    /// <summary> Признак отсутствия подписки. </summary>
    public bool NotFound { get; init; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Итоговое сообщение об операции. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Дата окончания подписки после операции. </summary>
    public DateTime? EndDate { get; init; }
}

