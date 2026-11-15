namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Краткие данные незавершённой записи, назначенной на рабочий пост. </summary>
public sealed class WorkPostAffectedMaintenanceDto
{
    /// <summary> Идентификатор записи. </summary>
    public Guid MaintenanceId { get; init; }

    /// <summary> Плановая дата записи или дата обслуживания. </summary>
    public DateTime Date { get; init; }

    /// <summary> Имя или номер техники. </summary>
    public string? VehicleName { get; init; }

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> Код состояния обслуживания. </summary>
    public int Status { get; init; }
}
