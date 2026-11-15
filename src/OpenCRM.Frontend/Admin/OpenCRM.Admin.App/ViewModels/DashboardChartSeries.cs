namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Серия графика дашборда с точками и оформлением. </summary>
public sealed record DashboardChartSeries(
    string Title,
    string Color,
    string Format,
    IReadOnlyList<DashboardChartPoint> Points);
