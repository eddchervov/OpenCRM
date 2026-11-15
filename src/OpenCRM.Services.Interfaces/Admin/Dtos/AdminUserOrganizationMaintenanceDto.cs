namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO обслуживания организации пользователя. </summary>
public sealed class AdminUserOrganizationMaintenanceDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; init; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; init; }

    /// <summary> Название техники обслуживания. </summary>
    public string VehicleName { get; init; } = string.Empty;

    /// <summary> Название статуса обслуживания. </summary>
    public string StatusName { get; init; } = string.Empty;
}
