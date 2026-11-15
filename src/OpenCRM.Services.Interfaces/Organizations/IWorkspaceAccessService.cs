using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>Объединяет тарифные возможности, роль и разрешения пользователя рабочего пространства.</summary>
public interface IWorkspaceAccessService
{
    /// <summary> Возвращает права доступа пользователя к рабочему пространству. </summary>
    Task<OrganizationWorkspaceAccessDto> GetWorkspaceAccessAsync(string userId, Guid organizationId);

    /// <summary> Проверяет право чтения в рабочем пространстве, иначе сообщает об ошибке. </summary>
    Task EnsureWorkspaceCanReadAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary> Проверяет право записи в рабочем пространстве, иначе сообщает об ошибке. </summary>
    Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId);

    /// <summary> Проверяет указанное право записи в рабочем пространстве. </summary>
    Task EnsureWorkspaceCanWriteAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary> Возвращает сводку доступа к обзору рабочего пространства. </summary>
    Task<WorkspaceOverviewAccessDto> GetOverviewAsync(string userId, Guid organizationId);
}
