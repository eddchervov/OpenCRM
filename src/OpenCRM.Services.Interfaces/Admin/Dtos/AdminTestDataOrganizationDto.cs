using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO тестовой организации. </summary>
public sealed class AdminTestDataOrganizationDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; init; }

    /// <summary> Название организации. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Идентификатор пользователя-владельца. </summary>
    public string OwnerUserId { get; init; } = string.Empty;

    /// <summary> Email владельца. </summary>
    public string? OwnerEmail { get; init; }

    /// <summary> Код сценария организации. </summary>
    public string ScenarioCode { get; init; } = string.Empty;

    /// <summary> Признак активной организации. </summary>
    public bool IsActive { get; init; }

    /// <summary> Количество техники организации. </summary>
    public int VehiclesCount { get; init; }

    /// <summary> Количество обслуживаний организации. </summary>
    public int MaintenanceCount { get; init; }
}

