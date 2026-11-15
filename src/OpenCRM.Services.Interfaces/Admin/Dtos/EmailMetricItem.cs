namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Значение метрики по коду статуса. </summary>
public sealed class EmailMetricItem
{
    /// <summary> Код статуса. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Количество в метрике. </summary>
    public int Count { get; init; }
}
