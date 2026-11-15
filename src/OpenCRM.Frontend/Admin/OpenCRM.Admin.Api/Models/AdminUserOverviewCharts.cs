namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные графиков обзора пользователя. </summary>
public sealed class AdminUserOverviewCharts
{
    /// <summary> Количество регистраций по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyCount> Registrations { get; init; } = [];

    /// <summary> Количество обслуживаний по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyCount> Maintenances { get; init; } = [];

    /// <summary> Суммы платежей по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyMoney> Payments { get; init; } = [];
}
