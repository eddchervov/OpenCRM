namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO графиков обзора пользователей. </summary>
public sealed class AdminUserOverviewChartsDto
{
    /// <summary> Регистрации по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyCountDto> Registrations { get; init; } = [];

    /// <summary> Обслуживания по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyCountDto> Maintenances { get; init; } = [];

    /// <summary> Платежи по дням. </summary>
    public IReadOnlyList<AdminUserOverviewDailyMoneyDto> Payments { get; init; } = [];
}
