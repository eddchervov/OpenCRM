namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Доступность сотрудника для смены. </summary>
public sealed class OrganizationShiftAvailabilityDto
{
    /// <summary> Признак доступности сотрудника, если задан. </summary>
    public bool? IsAvailable { get; set; }
}
