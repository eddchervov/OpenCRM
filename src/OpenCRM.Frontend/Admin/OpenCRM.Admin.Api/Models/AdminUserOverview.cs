namespace OpenCRM.Admin.Api.Models;

/// <summary> Сводная информация о пользователе для администрирования. </summary>
public sealed class AdminUserOverview
{
    /// <summary> Момент формирования сводки. </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary> Профиль пользователя. </summary>
    public AdminUserOverviewProfile Profile { get; init; } = new();

    /// <summary> Ключевые показатели пользователя. </summary>
    public AdminUserOverviewKpis Kpis { get; init; } = new();

    /// <summary> Данные для графиков пользователя. </summary>
    public AdminUserOverviewCharts Charts { get; init; } = new();

    /// <summary> Транспортные средства пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewVehicle> Vehicles { get; init; } = [];

    /// <summary> Обслуживания пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewMaintenance> Maintenances { get; init; } = [];

    /// <summary> Организации пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewOrganization> Organizations { get; init; } = [];

    /// <summary> Список приглашений пользователя в организации. </summary>
    public IReadOnlyList<AdminUserOverviewInvitation> Invitations { get; init; } = [];

    /// <summary> Подписки пользователя и его организаций. </summary>
    public AdminUserOverviewSubscriptions Subscriptions { get; init; } = new();

    /// <summary> Список платежей пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewPayment> Payments { get; init; } = [];

    /// <summary> Список выданных пользователю возможностей. </summary>
    public IReadOnlyList<AdminUserOverviewFeatureGrant> FeatureGrants { get; init; } = [];

    /// <summary> Обращения пользователя в поддержку. </summary>
    public IReadOnlyList<AdminUserOverviewSupportMessage> SupportMessages { get; init; } = [];

    /// <summary> Записи хронологии событий пользователя. </summary>
    public IReadOnlyList<AdminUserTimelineItem> Timeline { get; init; } = [];
}
