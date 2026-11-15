namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Сводка планирования рабочего пространства организации. </summary>
public sealed class OrganizationWorkspacePlanningSummary
{
    /// <summary> Количество просроченных записей. </summary>
    public int OverdueCount { get; init; }

    /// <summary> Количество записей с приближающимся сроком. </summary>
    public int SoonCount { get; init; }

    /// <summary> Количество записей, требующих внимания. </summary>
    public int AttentionCount { get; init; }

    /// <summary> Количество записей по дефектам. </summary>
    public int DefectsCount { get; init; }

    /// <summary> Количество записей первого обслуживания. </summary>
    public int FirstServiceCount { get; init; }

    /// <summary> Количество записей обновления счётчиков. </summary>
    public int CounterUpdateCount { get; init; }
}
