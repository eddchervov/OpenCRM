namespace OpenCRM.Admin.Api.Models;

/// <summary> Ключевые показатели обзора пользователя. </summary>
public sealed class AdminUserOverviewKpis
{
    /// <summary> Общее число транспортных средств. </summary>
    public int VehiclesTotal { get; init; }

    /// <summary> Число активных транспортных средств. </summary>
    public int VehiclesActive { get; init; }

    /// <summary> Общее число обслуживаний. </summary>
    public int MaintenancesTotal { get; init; }

    /// <summary> Общее число организаций. </summary>
    public int OrganizationsTotal { get; init; }

    /// <summary> Число платежей. </summary>
    public int PaymentsTotal { get; init; }

    /// <summary> Сумма платежей. </summary>
    public decimal PaymentsAmount { get; init; }

    /// <summary> Число активных подписок пользователя. </summary>
    public int ActiveUserSubscriptions { get; init; }

    /// <summary> Число активных подписок организаций. </summary>
    public int ActiveOrganizationSubscriptions { get; init; }

    /// <summary> Число активных выданных возможностей. </summary>
    public int ActiveFeatureGrants { get; init; }

    /// <summary> Число открытых обращений в поддержку. </summary>
    public int SupportOpen { get; init; }
}
