namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO организации в обзоре пользователя. </summary>
public sealed class AdminUserOverviewOrganizationDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string OrganizationName { get; init; } = string.Empty;

    /// <summary> Код основного сценария организации. </summary>
    public string PrimaryScenarioCode { get; init; } = string.Empty;

    /// <summary> Название сценария организации. </summary>
    public string ScenarioName { get; init; } = string.Empty;

    /// <summary> Код роли пользователя в организации. </summary>
    public string RoleCode { get; init; } = string.Empty;

    /// <summary> Название роли пользователя в организации. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Дата присоединения пользователя к организации. </summary>
    public DateTime JoinedAt { get; init; }

    /// <summary> Дата создания организации. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Идентификатор владельца организации. </summary>
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary> Признак владельца организации. </summary>
    public bool IsOwner { get; init; }

    /// <summary> Признак активной организации. </summary>
    public bool IsActive { get; init; }

    /// <summary> Признак архивной организации. </summary>
    public bool IsArchived { get; init; }

    /// <summary> Признак удалённой организации. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Количество участников организации. </summary>
    public int MembersCount { get; init; }

    /// <summary> Количество техники организации. </summary>
    public int VehiclesCount { get; init; }

    /// <summary> Количество клиентов организации. </summary>
    public int ClientsCount { get; init; }

    /// <summary> Название текущего тарифа организации. </summary>
    public string CurrentPlanName { get; init; } = string.Empty;

    /// <summary> Идентификатор текущей подписки организации. </summary>
    public int? CurrentSubscriptionId { get; init; }

    /// <summary> Дата окончания текущего тарифа. </summary>
    public DateTime? CurrentPlanEndDate { get; init; }

    /// <summary> Признак демо-тарифа. </summary>
    public bool CurrentPlanIsDemo { get; init; }

    /// <summary> Признак активной подписки организации. </summary>
    public bool HasActiveSubscription { get; init; }
}
