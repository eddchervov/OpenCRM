using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис управления правами администратора. </summary>
public sealed class AdminPermissionService(OpenCRMContext opencrmContext) : IAdminPermissionService
{
    /// <summary> Имя системной роли. </summary>
    private const string SystemRoleName = "System";

    /// <summary> Статический перечень прав. </summary>
    private static readonly IReadOnlyList<AdminPermissionDto> s_permissions =
    [
        CreatePermission(AdminAuthorizationPolicies.Access, "Доступ к административному модулю", "Общее"),
        CreatePermission(AdminAuthorizationPolicies.ViewDashboard, "Просмотр дашборда", "Дашборд"),
        CreatePermission(AdminAuthorizationPolicies.ViewUsers, "Просмотр пользователей", "Пользователи"),
        CreatePermission(AdminAuthorizationPolicies.ManageUsers, "Управление пользователями", "Пользователи"),
        CreatePermission(AdminAuthorizationPolicies.ImpersonateUsers, "Вход от имени пользователя", "Пользователи"),
        CreatePermission(AdminAuthorizationPolicies.ViewBilling, "Просмотр платежей и подписок", "Платежи"),
        CreatePermission(AdminAuthorizationPolicies.ManageBilling, "Управление платежами и подписками", "Платежи"),
        CreatePermission(AdminAuthorizationPolicies.ViewContent, "Просмотр контента и обращений", "Контент"),
        CreatePermission(AdminAuthorizationPolicies.ManageContent, "Управление контентом и обращениями", "Контент"),
        CreatePermission(AdminAuthorizationPolicies.ViewCommunications, "Просмотр сообщений и рассылок", "Коммуникации"),
        CreatePermission(AdminAuthorizationPolicies.ManageCommunications, "Управление сообщениями и рассылками", "Коммуникации"),
        CreatePermission(AdminAuthorizationPolicies.ManageConfiguration, "Управление настройками", "Настройки")
    ];

    /// <summary> Множество кодов всех прав. </summary>
    private static readonly HashSet<string> s_permissionCodes = s_permissions
        .Select(permission => permission.Code)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает перечень прав и их распределение по ролям. </summary>
    public async Task<AdminPermissionsDto> GetAsync()
    {
        var assignments = await this._opencrmContext.AdminRolePermissions
            .AsNoTracking()
            .Where(permission => s_permissionCodes.Contains(permission.PermissionCode))
            .GroupBy(permission => permission.RoleId)
            .ToDictionaryAsync(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(permission => permission.PermissionCode)
                    .OrderBy(permissionCode => permissionCode)
                    .ToList());

        var roles = await this._opencrmContext.Roles
            .AsNoTracking()
            .Select(role => new { role.Id, role.Name })
            .ToListAsync();

        var rolePermissions = roles
            .Select(role => new AdminRolePermissionsDto
            {
                RoleId = role.Id,
                RoleName = role.Name ?? string.Empty,
                IsLocked = string.Equals(role.Name, SystemRoleName, StringComparison.OrdinalIgnoreCase),
                PermissionCodes = string.Equals(role.Name, SystemRoleName, StringComparison.OrdinalIgnoreCase)
                    ? AdminAuthorizationPolicies.All
                    : assignments.GetValueOrDefault(role.Id, [])
            })
            .OrderByDescending(role => role.PermissionCodes.Count)
            .ThenByDescending(role => role.IsLocked)
            .ThenBy(role => role.RoleName)
            .ToList();

        return new AdminPermissionsDto
        {
            Permissions = s_permissions,
            Roles = rolePermissions
        };
    }

    /// <summary> Обновляет права роли. </summary>
    public async Task<AdminOperationResult> UpdateRolePermissionsAsync(
        string roleId,
        IReadOnlyCollection<string> permissionCodes,
        string? updatedByUserId)
    {
        var role = await this._opencrmContext.Roles.FirstOrDefaultAsync(item => item.Id == roleId);
        if (role == null)
        {
            return AdminOperationResult.Missing("Роль не найдена.");
        }

        if (string.Equals(role.Name, SystemRoleName, StringComparison.OrdinalIgnoreCase))
        {
            return AdminOperationResult.Invalid("Права системной роли нельзя изменять.");
        }

        var normalizedCodes = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var unknownCodes = normalizedCodes.Where(code => !s_permissionCodes.Contains(code)).ToArray();
        if (unknownCodes.Length > 0)
        {
            return AdminOperationResult.Invalid($"Неизвестные права: {string.Join(", ", unknownCodes)}.");
        }

        AddRequiredPermissions(normalizedCodes);

        var currentPermissions = await this._opencrmContext.AdminRolePermissions
            .Where(permission => permission.RoleId == roleId)
            .ToListAsync();

        this._opencrmContext.AdminRolePermissions.RemoveRange(currentPermissions);
        await this._opencrmContext.AdminRolePermissions.AddRangeAsync(normalizedCodes.Select(code =>
            new AdminRolePermission
            {
                RoleId = roleId,
                PermissionCode = code,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = updatedByUserId
            }));
        await this._opencrmContext.SaveChangesAsync();

        return AdminOperationResult.Success(message: "Права роли обновлены.");
    }

    /// <summary> Добавляет обязательные права в набор. </summary>
    private static void AddRequiredPermissions(HashSet<string> permissionCodes)
    {
        if (permissionCodes.Count == 0)
        {
            return;
        }

        permissionCodes.Add(AdminAuthorizationPolicies.Access);

        if (permissionCodes.Contains(AdminAuthorizationPolicies.ManageUsers) ||
            permissionCodes.Contains(AdminAuthorizationPolicies.ImpersonateUsers))
        {
            permissionCodes.Add(AdminAuthorizationPolicies.ViewUsers);
        }

        if (permissionCodes.Contains(AdminAuthorizationPolicies.ManageBilling))
        {
            permissionCodes.Add(AdminAuthorizationPolicies.ViewBilling);
        }

        if (permissionCodes.Contains(AdminAuthorizationPolicies.ManageContent))
        {
            permissionCodes.Add(AdminAuthorizationPolicies.ViewContent);
        }

        if (permissionCodes.Contains(AdminAuthorizationPolicies.ManageCommunications))
        {
            permissionCodes.Add(AdminAuthorizationPolicies.ViewCommunications);
        }
    }

    /// <summary> Создаёт описание права. </summary>
    private static AdminPermissionDto CreatePermission(string code, string name, string group)
    {
        return new AdminPermissionDto
        {
            Code = code,
            Name = name,
            Group = group
        };
    }
}
