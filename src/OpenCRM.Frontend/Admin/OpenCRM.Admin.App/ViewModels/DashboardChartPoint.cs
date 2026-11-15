namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Точка графика дашборда с подписью и значением. </summary>
public sealed record DashboardChartPoint(string Label, double? Value);
