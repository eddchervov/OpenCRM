namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные для графиков административной панели. </summary>
public sealed class AdminDashboardCharts
{
    /// <summary> Точки регистрации пользователей по дням. </summary>
    public IReadOnlyList<AdminDashboardCountPoint> Registrations { get; init; } = [];

    /// <summary> Точки входов в систему по дням. </summary>
    public IReadOnlyList<AdminDashboardCountPoint> Logins { get; init; } = [];

    /// <summary> Точки обслуживаний по дням. </summary>
    public IReadOnlyList<AdminDashboardCountPoint> Maintenances { get; init; } = [];

    /// <summary> Точки отправки писем по дням. </summary>
    public IReadOnlyList<AdminDashboardCountPoint> Emails { get; init; } = [];

    /// <summary> Точки платежей по дням. </summary>
    public IReadOnlyList<AdminDashboardMoneyPoint> Payments { get; init; } = [];
}
