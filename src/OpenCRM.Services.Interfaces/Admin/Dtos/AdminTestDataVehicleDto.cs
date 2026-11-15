using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO тестовой техники. </summary>
public sealed class AdminTestDataVehicleDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; init; }

    /// <summary> Подпись техники для выбора в интерфейсе. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Название бренда. </summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary> Название модели. </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary> Название техники. </summary>
    public string? Name { get; init; }

    /// <summary> Год выпуска. </summary>
    public int? YearIssue { get; init; }

    /// <summary> Признак архивной техники. </summary>
    public bool IsArchived { get; init; }

    /// <summary> Количество обслуживаний техники. </summary>
    public int MaintenanceCount { get; init; }

    /// <summary> Количество дефектов техники. </summary>
    public int DefectCount { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string? OrganizationName { get; init; }
}

