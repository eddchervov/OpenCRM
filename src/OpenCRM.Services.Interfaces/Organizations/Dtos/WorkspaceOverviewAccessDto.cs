using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary>Содержит проверенные возможности и тарифные ограничения обзора.</summary>
public sealed class WorkspaceOverviewAccessDto
{
    /// <summary> Данные о доступе к рабочему пространству обзора. </summary>
    public OrganizationWorkspaceAccessDto WorkspaceAccess { get; init; } = new();

    /// <summary> Снимок доступа к функциям. </summary>
    public FeatureAccessSnapshotDto FeatureSnapshot { get; init; } = new();

    /// <summary> Признак доступности модуля дефектов. </summary>
    public bool HasDefectsModule { get; init; }

    /// <summary> Признак доступности модуля счётчиков. </summary>
    public bool HasCountersModule { get; init; }

    /// <summary> Признак доступности модуля планировщика. </summary>
    public bool HasSchedulerModule { get; init; }

    /// <summary> Признак возможности просмотра транспортных средств. </summary>
    public bool CanViewVehicles { get; init; }

    /// <summary> Признак возможности просмотра аналитики. </summary>
    public bool CanViewAnalytics { get; init; }

    /// <summary> Признак возможности просмотра клиентов. </summary>
    public bool CanViewClients { get; init; }
}
