using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Обработчик авторизации по правам администратора. </summary>
public sealed class AdminPermissionAuthorizationHandler(OpenCRMContext opencrmContext)
    : AuthorizationHandler<AdminPermissionRequirement>
{
    /// <summary> Имя системной роли. </summary>
    private const string SystemRoleName = "System";

    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Проверяет наличие требуемого права у пользователя. </summary>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("UserID")
            ?? context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue("nameid");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var roleIds = await (
            from user in this._opencrmContext.Users.AsNoTracking()
            join userRole in this._opencrmContext.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in this._opencrmContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.Id == userId && !user.IsDeleted
            select new { role.Id, role.Name })
            .ToListAsync();
        if (roleIds.Count == 0)
        {
            return;
        }

        var isSystem = roleIds.Any(role => string.Equals(role.Name, SystemRoleName, StringComparison.OrdinalIgnoreCase));
        if (isSystem)
        {
            context.Succeed(requirement);
            return;
        }

        if (requirement.PermissionCode == AdminAuthorizationPolicies.ManagePermissions)
        {
            return;
        }

        var assignedRoleIds = roleIds.Select(role => role.Id).ToArray();
        var hasPermission = await this._opencrmContext.AdminRolePermissions
            .AsNoTracking()
            .AnyAsync(permission =>
                permission.PermissionCode == requirement.PermissionCode &&
                assignedRoleIds.Contains(permission.RoleId));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
