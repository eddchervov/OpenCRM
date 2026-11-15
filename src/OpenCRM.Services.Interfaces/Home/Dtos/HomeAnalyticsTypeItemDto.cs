namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Элемент статистики по виду обслуживания. </summary>
public class HomeAnalyticsTypeItemDto
{
    /// <summary> Наименование вида обслуживания. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Количество обслуживаний вида. </summary>
    public int Count { get; set; }

    /// <summary> Доля вида обслуживания в процентах. </summary>
    public int Percent { get; set; }
}
