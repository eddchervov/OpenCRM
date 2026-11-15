using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт первоначальной настройки организации. </summary>
public interface IOrganizationSetupService
{
    /// <summary> Возвращает состояние настройки организации. </summary>
    Task<OrganizationSetupDto> GetSetupAsync(string userId, Guid organizationId);

    /// <summary> Пытается открыть настройку организации. </summary>
    Task<bool> TryOpenSetupAsync(string userId, Guid organizationId);

    /// <summary> Сохраняет шаг настройки организации. </summary>
    Task SaveSetupAsync(string userId, Guid organizationId, SaveOrganizationSetupDto dto);
}
