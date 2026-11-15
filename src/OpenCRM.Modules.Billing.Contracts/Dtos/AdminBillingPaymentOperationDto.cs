namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Результат операции над платежом в админке биллинга. </summary>
public sealed class AdminBillingPaymentOperationDto
{
    /// <summary> Признак успешного выполнения операции. </summary>
    public bool Succeeded { get; init; }

    /// <summary> Признак того, что платеж не найден. </summary>
    public bool NotFound { get; init; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Сообщение о результате операции. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Статус платежа после операции. </summary>
    public string? Status { get; init; }

    /// <summary> Название статуса платежа. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Группа статуса платежа. </summary>
    public string StatusGroup { get; init; } = string.Empty;

    /// <summary> Статус платежа со стороны внешней системы. </summary>
    public string? RemoteStatus { get; init; }

    /// <summary> Признак оплаченного платежа. </summary>
    public bool Paid { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }
}

