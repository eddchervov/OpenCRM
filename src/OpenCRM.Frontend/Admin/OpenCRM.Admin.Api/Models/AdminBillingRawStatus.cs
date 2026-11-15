namespace OpenCRM.Admin.Api.Models;

/// <summary> Разбивка платежей по исходному статусу. </summary>
public sealed class AdminBillingRawStatus
{
    /// <summary> Исходный статус платежа. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Отображаемое название статуса. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Группа статуса. </summary>
    public string Group { get; init; } = string.Empty;

    /// <summary> Количество платежей в статусе. </summary>
    public int Count { get; init; }

    /// <summary> Сумма платежей в статусе. </summary>
    public decimal Amount { get; init; }
}
