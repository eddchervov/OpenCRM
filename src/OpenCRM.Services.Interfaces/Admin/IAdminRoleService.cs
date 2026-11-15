using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Управление ролями и их участниками. </summary>
public interface IAdminRoleService
{
    /// <summary> Возвращает данные о ролях. </summary>
    Task<AdminRolesDto> GetAsync();

    /// <summary> Создаёт роль с указанным названием. </summary>
    Task<AdminOperationResult> CreateAsync(string name);

    /// <summary> Обновляет название роли. </summary>
    Task<AdminOperationResult> UpdateAsync(string roleId, string name);

    /// <summary> Удаляет роль по идентификатору. </summary>
    Task<AdminOperationResult> DeleteAsync(string roleId);

    /// <summary> Возвращает участников роли. </summary>
    Task<AdminRoleMembersResultDto> GetMembersAsync(string roleId, string currentUserId);

    /// <summary> Добавляет пользователя в роль. </summary>
    Task<AdminOperationResult> AddMemberAsync(string roleId, string userId);

    /// <summary> Удаляет пользователя из роли. </summary>
    Task<AdminOperationResult> RemoveMemberAsync(string roleId, string userId, string currentUserId);
}
