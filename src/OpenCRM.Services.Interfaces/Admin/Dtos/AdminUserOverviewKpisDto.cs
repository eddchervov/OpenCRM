namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO ключевых показателей обзора пользователя. </summary>
public sealed class AdminUserOverviewKpisDto
{
    /// <summary> Общее количество техники. </summary>
    public int VehiclesTotal { get; init; }

    /// <summary> Количество активной техники. </summary>
    public int VehiclesActive { get; init; }

    /// <summary> Общее количество обслуживаний. </summary>
    public int MaintenancesTotal { get; init; }

    /// <summary> Общее количество организаций. </summary>
    public int OrganizationsTotal { get; init; }

    /// <summary> Общее количество платежей. </summary>
    public int PaymentsTotal { get; init; }

    /// <summary> Сумма платежей. </summary>
    public decimal PaymentsAmount { get; init; }

    /// <summary> Количество активных подписок пользователя. </summary>
    public int ActiveUserSubscriptions { get; init; }

    /// <summary> Количество активных подписок организаций. </summary>
    public int ActiveOrganizationSubscriptions { get; init; }

    /// <summary> Количество активных предоставлений функций. </summary>
    public int ActiveFeatureGrants { get; init; }

    /// <summary> Количество открытых обращений в поддержку. </summary>
    public int SupportOpen { get; init; }
}
