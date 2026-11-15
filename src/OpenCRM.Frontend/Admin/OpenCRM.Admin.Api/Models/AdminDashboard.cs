namespace OpenCRM.Admin.Api.Models;

/// <summary> Сводные данные административной панели. </summary>
public sealed class AdminDashboard
{
    /// <summary> Момент формирования сводки. </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary> Анализируемый период. </summary>
    public AdminDashboardPeriod Period { get; init; } = new();

    /// <summary> Ключевые показатели панели. </summary>
    public AdminDashboardKpis Kpis { get; init; } = new();

    /// <summary> Секции, требующие внимания администратора. </summary>
    public AdminDashboardAttention Attention { get; init; } = new();

    /// <summary> Данные для графиков панели. </summary>
    public AdminDashboardCharts Charts { get; init; } = new();

    /// <summary> Блок недавних событий. </summary>
    public AdminDashboardRecent Recent { get; init; } = new();
}
