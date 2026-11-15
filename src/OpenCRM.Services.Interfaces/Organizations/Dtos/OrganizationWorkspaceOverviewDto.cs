using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>Содержит данные обзора, разрешённые пользователю организации.</summary>
public sealed class OrganizationWorkspaceOverviewDto
{
    /// <summary> Данные организации. </summary>
    public OrganizationDto Organization { get; init; } = new();

    /// <summary> Данные о доступе к рабочему пространству. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; init; } = new();

    /// <summary> Доступные функции рабочего пространства. </summary>
    public IReadOnlyList<FeatureValueDto> WorkspaceFeatures { get; init; } = [];

    /// <summary> Текущая подписка, если есть. </summary>
    public OrganizationSubscriptionDto? CurrentSubscription { get; init; }

    /// <summary> Действующий тарифный план, если есть. </summary>
    public SubscriptionPlanDto? EffectivePlan { get; init; }

    /// <summary> Лимит транспортных средств рабочего пространства. </summary>
    public int WorkspaceVehicleLimit { get; init; }

    /// <summary> Лимит участников рабочего пространства. </summary>
    public int WorkspaceMembersLimit { get; init; }

    /// <summary> Количество активных транспортных средств. </summary>
    public int ActiveVehiclesCount { get; init; }

    /// <summary> Количество архивных транспортных средств. </summary>
    public int ArchivedVehiclesCount { get; init; }

    /// <summary> Количество клиентов. </summary>
    public int ClientsCount { get; init; }

    /// <summary> Метрики обзора рабочего пространства. </summary>
    public OrganizationWorkspaceOverviewMetrics Metrics { get; init; } = new();

    /// <summary> Сводка планирования рабочего пространства. </summary>
    public OrganizationWorkspacePlanningSummary Planning { get; init; } = new();
}
