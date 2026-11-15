namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO обзора пользователя в админ-панели. </summary>
public sealed class AdminUserOverviewDto
{
    /// <summary> Дата формирования обзора. </summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary> Профиль пользователя. </summary>
    public AdminUserOverviewProfileDto Profile { get; init; } = new();

    /// <summary> Ключевые показатели пользователя. </summary>
    public AdminUserOverviewKpisDto Kpis { get; init; } = new();

    /// <summary> Графики обзора пользователя. </summary>
    public AdminUserOverviewChartsDto Charts { get; init; } = new();

    /// <summary> Список техники пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewVehicleDto> Vehicles { get; init; } = [];

    /// <summary> Список обслуживаний пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewMaintenanceDto> Maintenances { get; init; } = [];

    /// <summary> Список организаций пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewOrganizationDto> Organizations { get; init; } = [];

    /// <summary> Список приглашений пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewInvitationDto> Invitations { get; init; } = [];

    /// <summary> Подписки пользователя и организаций. </summary>
    public AdminUserOverviewSubscriptionsDto Subscriptions { get; init; } = new();

    /// <summary> Список платежей пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewPaymentDto> Payments { get; init; } = [];

    /// <summary> Список предоставленных функций пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewFeatureGrantDto> FeatureGrants { get; init; } = [];

    /// <summary> Список сообщений поддержки пользователя. </summary>
    public IReadOnlyList<AdminUserOverviewSupportMessageDto> SupportMessages { get; init; } = [];

    /// <summary> Лента событий пользователя. </summary>
    public IReadOnlyList<AdminUserTimelineItemDto> Timeline { get; init; } = [];
}
