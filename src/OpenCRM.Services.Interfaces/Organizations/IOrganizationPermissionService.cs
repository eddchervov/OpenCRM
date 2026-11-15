using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// BL-контракт проверки и настройки прав фиксированных ролей внутри организации.
/// </summary>
public interface IOrganizationPermissionService
{
    /// <summary>
    /// Возвращает полный список доступных permission-кодов с русскими названиями.
    /// </summary>
    IReadOnlyList<OrganizationPermissionDefinitionDto> GetDefinitions();

    /// <summary>
    /// Возвращает дефолтную матрицу прав для owner, admin, employee и viewer.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> GetDefaultRoleMatrix();

    /// <summary>
    /// Возвращает фактическую матрицу прав организации с учетом локальных overrides.
    /// </summary>
    Task<IReadOnlyList<OrganizationRolePermissionMatrixDto>> GetRoleMatrixAsync(string userId, Guid organizationId);

    /// <summary>
    /// Проверяет, есть ли у пользователя конкретное право в организации.
    /// </summary>
    Task<bool> HasPermissionAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary>
    /// Требует право пользователя и выбрасывает ошибку доступа, если его нет.
    /// </summary>
    Task EnsurePermissionAsync(string userId, Guid organizationId, string permissionCode);

    /// <summary>
    /// Обновляет набор включенных прав для фиксированной роли организации.
    /// </summary>
    Task UpdateRolePermissionsAsync(string userId, Guid organizationId, UpdateOrganizationRolePermissionsDto dto);
}
