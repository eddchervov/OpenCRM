namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Статистика платежей по группе статусов. </summary>
public sealed class AdminBillingStatusGroupDto
{
    /// <summary> Отображаемое название группы статусов. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Код группы статусов. </summary>
    public string Group { get; init; } = string.Empty;

    /// <summary> Количество платежей в группе. </summary>
    public int Count { get; init; }

    /// <summary> Сумма платежей в группе. </summary>
    public decimal Amount { get; init; }
}

