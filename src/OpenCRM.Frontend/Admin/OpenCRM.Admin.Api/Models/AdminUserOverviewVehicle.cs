namespace OpenCRM.Admin.Api.Models;

/// <summary> Транспортное средство в обзоре пользователя. </summary>
public sealed class AdminUserOverviewVehicle
{
    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid Id { get; init; }

    /// <summary> Код вида транспортного средства. </summary>
    public int Kind { get; init; }

    /// <summary> Название вида транспортного средства. </summary>
    public string KindName { get; init; } = string.Empty;

    /// <summary> Название транспортного средства. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Марка транспортного средства. </summary>
    public string? Brand { get; init; }

    /// <summary> Модель транспортного средства. </summary>
    public string? Model { get; init; }

    /// <summary> Год выпуска. </summary>
    public int? YearIssue { get; init; }

    /// <summary> Регистрационный номер. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> VIN транспортного средства. </summary>
    public string? Vin { get; init; }

    /// <summary> Текущий пробег. </summary>
    public int? CurrentMileage { get; init; }

    /// <summary> Код единицы пробега. </summary>
    public int MileageUnit { get; init; }

    /// <summary> Название единицы пробега. </summary>
    public string MileageUnitName { get; init; } = string.Empty;

    /// <summary> Время создания записи. </summary>
    public DateTime DateTimeCreation { get; init; }

    /// <summary> Признак архивного транспортного средства. </summary>
    public bool IsArchived { get; init; }

    /// <summary> Признак удалённого транспортного средства. </summary>
    public bool IsDeleted { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary> Название организации. </summary>
    public string? OrganizationName { get; init; }

    /// <summary> Число обслуживаний. </summary>
    public int MaintenancesCount { get; init; }

    /// <summary> Число открытых дефектов. </summary>
    public int OpenDefectsCount { get; init; }

    /// <summary> Время последнего обслуживания. </summary>
    public DateTime? LastMaintenanceAt { get; init; }
}
