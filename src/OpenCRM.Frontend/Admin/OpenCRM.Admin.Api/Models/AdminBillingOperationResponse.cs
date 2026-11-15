namespace OpenCRM.Admin.Api.Models;

/// <summary> Результат операции над платежом. </summary>
public sealed class AdminBillingOperationResponse
{
    /// <summary> Сообщение о результате операции. </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary> Код актуального статуса платежа. </summary>
    public string? Status { get; init; }

    /// <summary> Название актуального статуса платежа. </summary>
    public string? StatusName { get; init; }

    /// <summary> Группа актуального статуса платежа. </summary>
    public string? StatusGroup { get; init; }

    /// <summary> Статус платежа в YooKassa. </summary>
    public string? RemoteStatus { get; init; }

    /// <summary> Признак подтверждённой оплаты. </summary>
    public bool? Paid { get; init; }

    /// <summary> Сумма платежа после синхронизации. </summary>
    public decimal? Amount { get; init; }
}
