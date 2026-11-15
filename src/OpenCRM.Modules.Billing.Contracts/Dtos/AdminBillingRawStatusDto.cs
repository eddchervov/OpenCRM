namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Статистика платежей по исходному статусу. </summary>
public sealed class AdminBillingRawStatusDto
{
    /// <summary> Код исходного статуса платежа. </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary> Отображаемое название статуса. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Код группы статусов. </summary>
    public string Group { get; init; } = string.Empty;

    /// <summary> Количество платежей в статусе. </summary>
    public int Count { get; init; }

    /// <summary> Сумма платежей в статусе. </summary>
    public decimal Amount { get; init; }
}

