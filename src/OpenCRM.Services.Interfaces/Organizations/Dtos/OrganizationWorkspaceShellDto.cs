using OpenCRM.Modules.Organizations.Contracts.Dtos;
namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Оболочка рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceShellDto
{
    /// <summary> Данные организации. </summary>
    public OrganizationDto Organization { get; init; } = new();

    /// <summary> Профиль сценария рабочего пространства. </summary>
    public OrganizationWorkspaceScenarioProfileDto Scenario { get; init; } = new();

    /// <summary> Пункты меню рабочего пространства. </summary>
    public IReadOnlyList<OrganizationWorkspaceMenuItemDto> Menu { get; init; } = [];

    /// <summary> Быстрые действия рабочего пространства. </summary>
    public IReadOnlyList<OrganizationWorkspaceQuickActionDto> QuickActions { get; init; } = [];
}
