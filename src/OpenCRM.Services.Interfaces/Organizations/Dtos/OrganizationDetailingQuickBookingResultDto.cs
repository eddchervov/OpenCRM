namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат быстрого создания записи детейлинга. </summary>
public class OrganizationDetailingQuickBookingResultDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор записи об обслуживании. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Идентификатор клиента организации. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Признак создания новой техники. </summary>
    public bool VehicleCreated { get; set; }

    /// <summary> Признак создания нового клиента. </summary>
    public bool ClientCreated { get; set; }

    /// <summary> URL страницы техники. </summary>
    public string VehicleUrl { get; set; } = string.Empty;

    /// <summary> URL страницы записи об обслуживании. </summary>
    public string MaintenanceUrl { get; set; } = string.Empty;
}
