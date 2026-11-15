using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис получения данных текущего администратора. </summary>
public sealed class AdminCurrentUserService(OpenCRMContext opencrmContext, IConfiguration? configuration = null) : IAdminCurrentUserService
{
    /// <summary> Имя системной роли. </summary>
    private const string SystemRoleName = "System";

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает данные текущего пользователя по идентификатору. </summary>
    public async Task<AdminCurrentUserDto?> GetAsync(string userId)
    {
        var user = await this._opencrmContext.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new { item.Id, item.Email })
            .FirstOrDefaultAsync();
        if (user == null)
        {
            return null;
        }

        var roles = await (
            from userRole in this._opencrmContext.UserRoles.AsNoTracking()
            join role in this._opencrmContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.Name != null
            orderby role.Name
            select new { role.Id, Name = role.Name! })
            .ToListAsync();

        var isSystem = roles.Any(role => string.Equals(role.Name, SystemRoleName, StringComparison.OrdinalIgnoreCase));
        var roleIds = roles.Select(role => role.Id).ToArray();

        var permissions = isSystem
            ? AdminAuthorizationPolicies.All
                .Append(AdminAuthorizationPolicies.ManagePermissions)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(permission => permission)
                .ToList()
            : await this._opencrmContext.AdminRolePermissions
                .AsNoTracking()
                .Where(permission => roleIds.Contains(permission.RoleId))
                .Select(permission => permission.PermissionCode)
                .Distinct()
                .OrderBy(permission => permission)
                .ToListAsync();

        return new AdminCurrentUserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Roles = roles.Select(role => role.Name).ToList(),
            Permissions = permissions,
            IsSystem = isSystem,
            CommercialEnabled = configuration?.GetValue("PlatformPolicy:Commercial:Enabled", false) ?? false
        };
    }
}
