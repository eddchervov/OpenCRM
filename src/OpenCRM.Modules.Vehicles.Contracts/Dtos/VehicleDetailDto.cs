using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Подробные данные техники. </summary>
public class VehicleDetailDto
{
    /// <summary> Идентификатор техники. </summary>
    public required Guid Id { get; set; }

    /// <summary> Вид техники. </summary>
    public required VehicleKind Kind { get; set; }

    /// <summary> Единица измерения пробега. </summary>
    public required VehicleMileageUnit MileageUnit { get; set; }

    /// <summary> Идентификатор бренда, если задан. </summary>
    public required int? BrandId { get; set; }

    /// <summary> Название бренда. </summary>
    public required string BrandName { get; set; }

    /// <summary> Идентификатор модели, если задана. </summary>
    public required int? ModelId { get; set; }

    /// <summary> Название модели. </summary>
    public required string ModelName { get; set; }

    /// <summary> Год выпуска техники. </summary>
    public required int? YearIssue { get; set; }

    /// <summary> Путь к фотографии техники. </summary>
    public required string? PhotoPath { get; set; }

    /// <summary> Название бренда, введённое вручную. </summary>
    public required string? ManualBrandName { get; set; }

    /// <summary> Название модели, введённое вручную. </summary>
    public required string? ManualModelName { get; set; }

    /// <summary> Название техники. </summary>
    public required string? Name { get; set; }

    /// <summary> Описание техники. </summary>
    public required string? Description { get; set; }

    /// <summary> Общее количество записей об обслуживании. </summary>
    public required int TotalMaintenanceCount { get; set; }

    /// <summary> Список обслуживаний техники. </summary>
    public required IEnumerable<VehicleMaintenanceDto> Maintenance { get; set; }
}
