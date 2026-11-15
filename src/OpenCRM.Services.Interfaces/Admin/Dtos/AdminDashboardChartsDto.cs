namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO графиков дашборда. </summary>
public sealed class AdminDashboardChartsDto
{
    /// <summary> График регистраций. </summary>
    public IReadOnlyList<AdminDashboardCountPointDto> Registrations { get; init; } = [];

    /// <summary> График входов. </summary>
    public IReadOnlyList<AdminDashboardCountPointDto> Logins { get; init; } = [];

    /// <summary> График обслуживаний. </summary>
    public IReadOnlyList<AdminDashboardCountPointDto> Maintenances { get; init; } = [];

    /// <summary> График писем. </summary>
    public IReadOnlyList<AdminDashboardCountPointDto> Emails { get; init; } = [];

    /// <summary> График платежей. </summary>
    public IReadOnlyList<AdminDashboardMoneyPointDto> Payments { get; init; } = [];
}

