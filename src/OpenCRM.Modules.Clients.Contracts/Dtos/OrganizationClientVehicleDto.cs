namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Транспортное средство клиента организации. </summary>
public class OrganizationClientVehicleDto
{
    /// <summary> Идентификатор транспортного средства. </summary>
    public Guid Id { get; set; }

    /// <summary> Отображаемое название транспортного средства. </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary> Ключ логотипа марки. </summary>
    public string? BrandLogoKey { get; set; }

    /// <summary> Ссылка на фото транспортного средства. </summary>
    public string? PhotoUrl { get; set; }

    /// <summary> Регистрационный номер. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Количество обслуживаний транспортного средства. </summary>
    public int MaintenanceCount { get; set; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastMaintenanceDate { get; set; }
}
