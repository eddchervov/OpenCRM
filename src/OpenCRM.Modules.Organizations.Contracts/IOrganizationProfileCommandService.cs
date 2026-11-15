using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт команд создания и обновления организаций. </summary>
public interface IOrganizationProfileCommandService
{
    /// <summary> Создаёт организацию от имени владельца. </summary>
    Task<Guid> CreateAsync(string ownerUserId, CreateOrganizationDto dto);

    /// <summary> Обновляет данные организации. </summary>
    Task UpdateAsync(string currentUserId, Guid organizationId, UpdateOrganizationDto dto);

    /// <summary> Обновляет профиль организации. </summary>
    Task UpdateProfileAsync(string currentUserId, Guid organizationId, UpdateOrganizationProfileDto dto);

    /// <summary> Обновляет реквизиты организации. </summary>
    Task UpdateRequisitesAsync(string currentUserId, Guid organizationId, UpdateOrganizationRequisitesDto dto);
}
