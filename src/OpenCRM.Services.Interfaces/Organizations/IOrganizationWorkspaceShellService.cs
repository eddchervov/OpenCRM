using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса оболочки рабочего пространства организации. </summary>
public interface IOrganizationWorkspaceShellService
{
    /// <summary> Возвращает данные оболочки рабочего пространства организации. </summary>
    Task<OrganizationWorkspaceShellDto> GetAsync(string userId, Guid organizationId);
}
