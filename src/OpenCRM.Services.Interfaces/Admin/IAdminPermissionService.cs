using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление разрешениями ролей. </summary>
public interface IAdminPermissionService
{
    /// <summary> Возвращает данные о разрешениях. </summary>
    Task<AdminPermissionsDto> GetAsync();

    /// <summary> Обновляет набор разрешений роли. </summary>
    Task<AdminOperationResult> UpdateRolePermissionsAsync(
        string roleId,
        IReadOnlyCollection<string> permissionCodes,
        string? updatedByUserId);
}
