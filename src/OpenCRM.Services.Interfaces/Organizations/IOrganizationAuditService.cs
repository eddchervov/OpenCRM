using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Записывает и читает аудит действий внутри организации.
/// </summary>
public interface IOrganizationAuditService
{
    /// <summary>
    /// Добавляет событие аудита организации.
    /// </summary>
    Task AddAsync(OrganizationAuditEventCreateDto dto, bool saveChanges = true);

    /// <summary>
    /// Возвращает последние события аудита, доступные пользователю организации.
    /// </summary>
    Task<IReadOnlyList<OrganizationAuditEventDto>> GetRecentAsync(string userId, Guid organizationId, int take = 20);
}
