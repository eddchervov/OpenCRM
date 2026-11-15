namespace OpenCRM.Admin.Api.Models;

/// <summary> Группа статусов платежей. </summary>
public sealed class AdminBillingStatusGroup
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
