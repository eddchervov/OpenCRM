using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт управления часовым поясом организации. </summary>
public interface IOrganizationTimeZoneService
{
    /// <summary> Возвращает часовой пояс организации для участника рабочего пространства. </summary>
    Task<OrganizationTimeZoneDto> GetAsync(string userId, Guid organizationId);

    /// <summary> Условно обновляет часовой пояс организации. </summary>
    Task<OrganizationTimeZoneDto> UpdateAsync(string userId, Guid organizationId, UpdateOrganizationTimeZoneDto dto);
}
