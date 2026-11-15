namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Краткие данные техники клиента. </summary>
public sealed class VehicleClientSummaryDto
{
    /// <summary> Идентификатор клиента. </summary>
    public Guid ClientId { get; init; }

    /// <summary> Идентификатор техники. </summary>
    public Guid Id { get; init; }

    /// <summary> Год выпуска техники. </summary>
    public int? YearIssue { get; init; }

    /// <summary> Название техники. </summary>
    public string? Name { get; init; }

    /// <summary> Регистрационный номер техники. </summary>
    public string? RegistrationPlate { get; init; }

    /// <summary> Название бренда. </summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary> Ключ логотипа марки. </summary>
    public string? BrandLogoKey { get; init; }

    /// <summary> Название модели. </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary> Количество записей об обслуживании. </summary>
    public int MaintenanceCount { get; init; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastMaintenanceDate { get; init; }
}
