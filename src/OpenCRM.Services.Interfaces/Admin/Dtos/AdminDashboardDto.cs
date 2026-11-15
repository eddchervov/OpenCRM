namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO дашборда администратора. </summary>
public sealed class AdminDashboardDto
{
    /// <summary> Время формирования данных. </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary> Данные периода. </summary>
    public AdminDashboardPeriodDto Period { get; init; } = new();

    /// <summary> Ключевые показатели. </summary>
    public AdminDashboardKpisDto Kpis { get; init; } = new();

    /// <summary> Показатели, требующие внимания. </summary>
    public AdminDashboardAttentionDto Attention { get; init; } = new();

    /// <summary> Данные графиков. </summary>
    public AdminDashboardChartsDto Charts { get; init; } = new();

    /// <summary> Данные последних записей. </summary>
    public AdminDashboardRecentDto Recent { get; init; } = new();
}
