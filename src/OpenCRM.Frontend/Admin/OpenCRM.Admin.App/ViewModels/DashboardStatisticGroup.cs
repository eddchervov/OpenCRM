namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Группа статистики с заголовком и метриками. </summary>
public sealed record DashboardStatisticGroup(string Title, IReadOnlyList<DashboardMetric> Items);
