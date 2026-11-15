using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис управления ролями и их участниками. </summary>
public sealed class AdminRoleService(
    OpenCRMContext opencrmContext,
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager) : IAdminRoleService
{
    /// <summary> Множество защищённых имён ролей. </summary>
    private static readonly HashSet<string> s_protectedRoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "System",
        "Moderator"
    };

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость менеджера ролей. </summary>
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;

    /// <summary> Зависимость менеджера пользователей. </summary>
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    /// <summary> Возвращает список ролей с правами и участниками. </summary>
    public async Task<AdminRolesDto> GetAsync()
    {
        var roles = await this._opencrmContext.Roles
            .OrderBy(role => role.Name)
            .ToListAsync();
        var roleIds = roles.Select(role => role.Id).ToList();

        var userCounts = await this._opencrmContext.UserRoles
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .GroupBy(userRole => userRole.RoleId)
            .Select(group => new
            {
                RoleId = group.Key,
                UsersCount = group.Count()
            })
            .ToDictionaryAsync(item => item.RoleId, item => item.UsersCount);

        var usersPreview = await this._opencrmContext.UserRoles
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Join(
                this._opencrmContext.ApplicationUsers,
                userRole => userRole.UserId,
                user => user.Id,
                (userRole, user) => new
                {
                    userRole.RoleId,
                    DisplayName = user.Email ?? user.PhoneNumber ?? user.Id
                })
            .OrderBy(item => item.DisplayName)
            .ToListAsync();

        var roleDtos = roles
            .Select(role => new AdminRoleDto
            {
                Id = role.Id,
                Name = role.Name ?? string.Empty,
                NormalizedName = role.NormalizedName,
                UsersCount = userCounts.TryGetValue(role.Id, out var count) ? count : 0,
                IsProtected = IsProtected(role),
                UsersPreview = usersPreview
                    .Where(user => user.RoleId == role.Id)
                    .Select(user => user.DisplayName)
                    .Take(3)
                    .ToList()
            })
            .ToList();

        return new AdminRolesDto
        {
            Roles = roleDtos,
            TotalRoles = roleDtos.Count,
            ProtectedRolesCount = roleDtos.Count(role => role.IsProtected),
            RolesWithUsersCount = roleDtos.Count(role => role.UsersCount > 0),
            TotalAssignmentsCount = roleDtos.Sum(role => role.UsersCount)
        };
    }

    /// <summary> Создаёт новую роль. </summary>
    public async Task<AdminOperationResult> CreateAsync(string name)
    {
        var roleName = name.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return AdminOperationResult.Invalid("Укажите название роли.");
        }

        var existingRole = await this._roleManager.FindByNameAsync(roleName);
        if (existingRole != null)
        {
            return AdminOperationResult.Invalid("Роль с таким названием уже существует.");
        }

        var result = await this._roleManager.CreateAsync(new IdentityRole(roleName));

        return result.Succeeded
            ? AdminOperationResult.Success("Роль успешно создана.")
            : AdminOperationResult.Invalid(GetErrors(result));
    }

    /// <summary> Переименовывает роль. </summary>
    public async Task<AdminOperationResult> UpdateAsync(string roleId, string name)
    {
        var role = await this._roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return AdminOperationResult.Missing("Роль не найдена.");
        }

        if (IsProtected(role))
        {
            return AdminOperationResult.Invalid("Системные роли нельзя переименовывать.");
        }

        var roleName = name.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return AdminOperationResult.Invalid("Укажите название роли.");
        }

        var duplicate = await this._roleManager.FindByNameAsync(roleName);
        if (duplicate != null && duplicate.Id != role.Id)
        {
            return AdminOperationResult.Invalid("Роль с таким названием уже существует.");
        }

        role.Name = roleName;
        role.NormalizedName = this._roleManager.NormalizeKey(roleName);

        var result = await this._roleManager.UpdateAsync(role);

        return result.Succeeded
            ? AdminOperationResult.Success("Название роли обновлено.")
            : AdminOperationResult.Invalid(GetErrors(result));
    }

    /// <summary> Удаляет роль. </summary>
    public async Task<AdminOperationResult> DeleteAsync(string roleId)
    {
        var role = await this._roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return AdminOperationResult.Missing("Роль не найдена.");
        }

        if (IsProtected(role))
        {
            return AdminOperationResult.Invalid("Системные роли нельзя удалять.");
        }

        var usersCount = await this._opencrmContext.UserRoles.CountAsync(userRole => userRole.RoleId == role.Id);
        if (usersCount > 0)
        {
            return AdminOperationResult.Invalid("Нельзя удалить роль, пока в ней есть пользователи.");
        }

        var result = await this._roleManager.DeleteAsync(role);

        return result.Succeeded
            ? AdminOperationResult.Success("Роль удалена.")
            : AdminOperationResult.Invalid(GetErrors(result));
    }

    /// <summary> Возвращает участников роли и доступных пользователей. </summary>
    public async Task<AdminRoleMembersResultDto> GetMembersAsync(string roleId, string currentUserId)
    {
        var role = await this._roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return new AdminRoleMembersResultDto
            {
                NotFound = true,
                ErrorMessage = "Роль не найдена."
            };
        }

        var roleUserIds = await this._opencrmContext.UserRoles
            .Where(userRole => userRole.RoleId == roleId)
            .Select(userRole => userRole.UserId)
            .ToListAsync();
        var users = await this._opencrmContext.ApplicationUsers
            .Where(user => !user.IsDeleted)
            .OrderBy(user => user.Email)
            .ThenBy(user => user.PhoneNumber)
            .Select(user => new AdminRoleMemberDto
            {
                UserId = user.Id,
                DisplayName = user.Email ?? user.PhoneNumber ?? user.Id,
                Email = user.Email,
                Phone = user.PhoneNumber,
                HasRole = roleUserIds.Contains(user.Id),
                IsCurrentUser = user.Id == currentUserId
            })
            .ToListAsync();

        return new AdminRoleMembersResultDto
        {
            RoleId = role.Id,
            RoleName = role.Name ?? string.Empty,
            IsProtected = IsProtected(role),
            Users = users
        };
    }

    /// <summary> Добавляет пользователя в роль. </summary>
    public async Task<AdminOperationResult> AddMemberAsync(string roleId, string userId)
    {
        var role = await this._roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return AdminOperationResult.Missing("Роль не найдена.");
        }

        var user = await this._userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return AdminOperationResult.Missing("Пользователь не найден.");
        }

        if (await this._userManager.IsInRoleAsync(user, role.Name!))
        {
            return AdminOperationResult.Invalid("У пользователя уже есть эта роль.");
        }

        var result = await this._userManager.AddToRoleAsync(user, role.Name!);

        return result.Succeeded
            ? AdminOperationResult.Success("Роль успешно назначена пользователю.")
            : AdminOperationResult.Invalid(GetErrors(result));
    }

    /// <summary> Удаляет пользователя из роли. </summary>
    public async Task<AdminOperationResult> RemoveMemberAsync(string roleId, string userId, string currentUserId)
    {
        var role = await this._roleManager.FindByIdAsync(roleId);
        if (role == null)
        {
            return AdminOperationResult.Missing("Роль не найдена.");
        }

        var user = await this._userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return AdminOperationResult.Missing("Пользователь не найден.");
        }

        if (user.Id == currentUserId && IsProtected(role))
        {
            return AdminOperationResult.Invalid("Нельзя снять защищенную роль с текущей учетной записи через эту страницу.");
        }

        if (!await this._userManager.IsInRoleAsync(user, role.Name!))
        {
            return AdminOperationResult.Invalid("У пользователя нет этой роли.");
        }

        var result = await this._userManager.RemoveFromRoleAsync(user, role.Name!);

        return result.Succeeded
            ? AdminOperationResult.Success("Роль снята с пользователя.")
            : AdminOperationResult.Invalid(GetErrors(result));
    }

    /// <summary> Проверяет, защищена ли роль от изменений. </summary>
    private static bool IsProtected(IdentityRole role)
    {
        return !string.IsNullOrWhiteSpace(role.Name) && s_protectedRoleNames.Contains(role.Name);
    }

    /// <summary> Собирает текст ошибок результата операции Identity. </summary>
    private static string GetErrors(IdentityResult result)
    {
        return string.Join(" ", result.Errors.Select(error => error.Description));
    }
}
