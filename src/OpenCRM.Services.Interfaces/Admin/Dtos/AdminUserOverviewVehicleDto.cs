using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO техники в обзоре пользователя. </summary>
public sealed class AdminUserOverviewVehicleDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Вид техники. </summary>
    public VehicleKind Kind { get; init; }

    /// <summary> Название вида техники. </summary>
    public string KindName { get; init; } = string.Empty;

    /// <summary> Название техники. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Бренд техники. </summary>
    public string? Brand { get; init; }

    /// <summary> Модель техники. </summary>
    public string? Model { get; init; }

    /// <summary> Год выпуска. </summary>
    public int? YearIssue { get; init; }

    /// <summary> Регистрационный номер. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> VIN техники. </summary>
    public string? Vin { get; init; }

    /// <summary> Текущий пробег. </summary>
    public int? CurrentMileage { get; init; }

    /// <summary> Единица измерения пробега. </summary>
    public VehicleMileageUnit MileageUnit { get; init; }

    /// <summary> Название единицы измерения пробега. </summary>
    public string MileageUnitName { get; init; } = string.Empty;

    /// <summary> Дата создания записи техники. </summary>
    public DateTime DateTimeCreation { get; init; }

    /// <summary> Признак архивной техники. </summary>
    public bool IsArchived { get; init; }

    /// <summary> Признак удалённой техники. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string? OrganizationName { get; init; }

    /// <summary> Количество обслуживаний техники. </summary>
    public int MaintenancesCount { get; init; }

    /// <summary> Количество открытых дефектов техники. </summary>
    public int OpenDefectsCount { get; init; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastMaintenanceAt { get; init; }
}
