using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Сервис разрешений и ролей организации. </summary>
public sealed class OrganizationPermissionService(
    OpenCRMContext context,
    IOrganizationAuditService organizationAuditService) : IOrganizationPermissionService
{
    /// <summary> Возвращает определения доступных разрешений. </summary>
    public IReadOnlyList<OrganizationPermissionDefinitionDto> GetDefinitions() =>
        OrganizationPermissionCatalog.GetDefinitions();

    /// <summary> Возвращает матрицу разрешений по умолчанию для ролей. </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> GetDefaultRoleMatrix() =>
        OrganizationPermissionCatalog.GetDefaultRoleMatrix();

    /// <summary> Возвращает матрицу разрешений ролей организации. </summary>
    public async Task<IReadOnlyList<OrganizationRolePermissionMatrixDto>> GetRoleMatrixAsync(string userId, Guid organizationId)
    {
        await EnsureCanReadAsync(userId, organizationId);

        var definitions = OrganizationPermissionCatalog.GetDefinitions();
        var result = new List<OrganizationRolePermissionMatrixDto>();

        foreach (var roleCode in OrganizationRoleCodes.All)
        {
            var permissions = await ResolveRolePermissionsAsync(organizationId, roleCode);
            result.Add(new OrganizationRolePermissionMatrixDto
            {
                RoleCode = roleCode,
                RoleName = GetRoleName(roleCode),
                CanEdit = roleCode is not OrganizationRoleCodes.Owner and not OrganizationRoleCodes.Viewer,
                Permissions = definitions
                    .Select(x => new OrganizationRolePermissionDto
                    {
                        Code = x.Code,
                        Name = x.Name,
                        GroupName = x.GroupName,
                        Description = x.Description,
                        IsWritePermission = x.IsWritePermission,
                        IsDefaultEnabled = OrganizationPermissionCatalog.RoleHasPermission(roleCode, x.Code),
                        IsEnabled = permissions.Contains(x.Code)
                    })
                    .ToList()
            });
        }

        return result;
    }

    /// <summary> Проверяет наличие разрешения у пользователя в организации. </summary>
    public async Task<bool> HasPermissionAsync(string userId, Guid organizationId, string permissionCode)
    {
        var roleCode = await context.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId && !x.IsDeleted && x.IsActive && !x.IsArchived)
            .SelectMany(x => x.Members.Where(member => member.UserId == userId))
            .Select(x => x.RoleCode)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(roleCode))
        {
            return false;
        }

        var permissions = await ResolveRolePermissionsAsync(organizationId, roleCode);
        return permissions.Contains(NormalizePermissionCode(permissionCode));
    }

    /// <summary> Требует наличие разрешения у пользователя, иначе ошибка. </summary>
    public async Task EnsurePermissionAsync(string userId, Guid organizationId, string permissionCode)
    {
        if (!await HasPermissionAsync(userId, organizationId, permissionCode))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(permissionCode));
        }
    }

    /// <summary> Обновляет разрешения роли организации. </summary>
    public async Task UpdateRolePermissionsAsync(string userId, Guid organizationId, UpdateOrganizationRolePermissionsDto dto)
    {
        if (context.Database.CurrentTransaction != null)
        {
            await UpdateRolePermissionsCoreAsync(userId, organizationId, dto);

            return;
        }

        var isRetry = false;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            await UpdateRolePermissionsCoreAsync(userId, organizationId, dto);
        }, async () =>
        {
            var requestedCodes = dto.EnabledPermissionCodes
                .Select(NormalizePermissionCode)
                .Where(code => OrganizationPermissionCatalog.GetDefinitions().Any(definition => definition.Code == code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            requestedCodes.Add(OrganizationPermissionCodes.WorkspaceView);
            var actualCodes = await ResolveRolePermissionsAsync(organizationId, NormalizeRoleCode(dto.RoleCode));

            return requestedCodes.SetEquals(actualCodes);
        });
    }

    /// <summary> Внутренняя реализация обновления разрешений роли. </summary>
    private async Task UpdateRolePermissionsCoreAsync(string userId, Guid organizationId, UpdateOrganizationRolePermissionsDto dto)
    {
        await EnsureCanManagePermissionsAsync(userId, organizationId);

        var roleCode = NormalizeRoleCode(dto.RoleCode);
        if (!OrganizationRoleCodes.All.Contains(roleCode))
        {
            throw new InvalidOperationException("Роль не найдена.");
        }

        if (roleCode == OrganizationRoleCodes.Owner)
        {
            throw new InvalidOperationException("Права владельца нельзя ограничить.");
        }

        if (roleCode == OrganizationRoleCodes.Viewer)
        {
            throw new InvalidOperationException("Роль просмотра остается только для чтения.");
        }

        var definitions = OrganizationPermissionCatalog.GetDefinitions();
        var knownCodes = definitions
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var enabledCodes = dto.EnabledPermissionCodes
            .Select(NormalizePermissionCode)
            .Where(knownCodes.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        enabledCodes.Add(OrganizationPermissionCodes.WorkspaceView);
        if (enabledCodes.Contains(OrganizationPermissionCodes.ShiftsManage)
            && !enabledCodes.Contains(OrganizationPermissionCodes.ShiftsView))
        {
            throw new InvalidOperationException("Для управления сменами включите просмотр смен.");
        }

        if (enabledCodes.Contains(OrganizationPermissionCodes.InquiriesManage)
            && !enabledCodes.Contains(OrganizationPermissionCodes.InquiriesView))
        {
            throw new InvalidOperationException("Для управления обращениями включите просмотр обращений.");
        }

        if ((enabledCodes.Contains(OrganizationPermissionCodes.MessagesSend)
                || enabledCodes.Contains(OrganizationPermissionCodes.MessagesManage)
                || enabledCodes.Contains(OrganizationPermissionCodes.MessagesTemplatesManage))
            && !enabledCodes.Contains(OrganizationPermissionCodes.MessagesView))
        {
            throw new InvalidOperationException("Для отправки сообщений и связи с клиентом включите просмотр переписки.");
        }

        if (!enabledCodes.Contains(OrganizationPermissionCodes.MaintenanceWorkExecute))
        {
            var employeeIds = await context.OrganizationEmployees.AsNoTracking()
                .Where(employee => employee.OrganizationId == organizationId
                    && employee.Member != null && employee.Member.RoleCode == roleCode)
                .Select(employee => employee.Id)
                .OrderBy(id => id)
                .ToListAsync();
            foreach (var employeeId in employeeIds)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employeeId} FOR UPDATE""");
            }

            if (await context.MaintenanceWorkOperations.AsNoTracking()
                .AnyAsync(operation => operation.OrganizationId == organizationId
                    && employeeIds.Contains(operation.EmployeeId)
                    && (operation.Status == MaintenanceWorkStatus.InProgress
                        || operation.Status == MaintenanceWorkStatus.Paused)))
            {
                throw new InvalidOperationException("Сначала завершите активные работы исполнителей, затем измените права роли.");
            }
        }

        var overrides = await context.OrganizationRolePermissionOverrides
            .Where(x => x.OrganizationId == organizationId && x.RoleCode == roleCode)
            .ToListAsync();

        var overridesByPermission = overrides.ToDictionary(x => x.PermissionCode, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var changedPermissions = new List<string>();

        foreach (var definition in definitions)
        {
            var defaultEnabled = OrganizationPermissionCatalog.RoleHasPermission(roleCode, definition.Code);
            var requestedEnabled = enabledCodes.Contains(definition.Code);
            var currentEnabled = overridesByPermission.TryGetValue(definition.Code, out var existingOverride)
                ? existingOverride.IsEnabled
                : defaultEnabled;

            if (currentEnabled != requestedEnabled)
            {
                changedPermissions.Add($"{definition.Name}: {(requestedEnabled ? "включено" : "выключено")}");
            }

            if (requestedEnabled == defaultEnabled)
            {
                if (existingOverride != null)
                {
                    context.OrganizationRolePermissionOverrides.Remove(existingOverride);
                }

                continue;
            }

            if (overridesByPermission.TryGetValue(definition.Code, out var permissionOverride))
            {
                permissionOverride.IsEnabled = requestedEnabled;
                permissionOverride.UpdatedAt = now;
                permissionOverride.UpdatedByUserId = userId;
                continue;
            }

            context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
            {
                OrganizationId = organizationId,
                RoleCode = roleCode,
                PermissionCode = definition.Code,
                IsEnabled = requestedEnabled,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = userId
            });
        }

        if (changedPermissions.Count > 0)
        {
            await organizationAuditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "permissions.role.updated",
                Title = $"Права роли изменены: {GetRoleName(roleCode)}",
                Description = string.Join("; ", changedPermissions),
                TargetType = "role",
                TargetId = roleCode
            }, saveChanges: false);
        }

        await context.SaveChangesAsync();
    }

    /// <summary> Возвращает набор разрешений роли организации. </summary>
    private async Task<IReadOnlySet<string>> ResolveRolePermissionsAsync(Guid organizationId, string roleCode)
    {
        var normalizedRoleCode = NormalizeRoleCode(roleCode);
        var permissions = OrganizationPermissionCatalog.GetDefaultRoleMatrix()
            .TryGetValue(normalizedRoleCode, out var defaultPermissions)
            ? defaultPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var overrides = await context.OrganizationRolePermissionOverrides
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.RoleCode == normalizedRoleCode)
            .ToListAsync();

        foreach (var permissionOverride in overrides)
        {
            var permissionCode = NormalizePermissionCode(permissionOverride.PermissionCode);
            if (permissionOverride.IsEnabled)
            {
                permissions.Add(permissionCode);
            }
            else
            {
                permissions.Remove(permissionCode);
            }
        }

        if (normalizedRoleCode != OrganizationRoleCodes.Owner)
        {
            permissions.Add(OrganizationPermissionCodes.WorkspaceView);
        }

        return permissions;
    }

    /// <summary> Проверяет право пользователя на чтение разрешений. </summary>
    private async Task EnsureCanReadAsync(string userId, Guid organizationId)
    {
        var canRead = await context.Organizations
            .AsNoTracking()
            .AnyAsync(x => x.Id == organizationId
                && !x.IsDeleted
                && !x.IsArchived
                && x.IsActive
                && x.Members.Any(member => member.UserId == userId));

        if (!canRead)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }
    }

    /// <summary> Проверяет право пользователя на управление разрешениями. </summary>
    private async Task EnsureCanManagePermissionsAsync(string userId, Guid organizationId)
    {
        var roleCode = await context.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId && !x.IsDeleted && !x.IsArchived && x.IsActive)
            .SelectMany(x => x.Members.Where(member => member.UserId == userId))
            .Select(x => x.RoleCode)
            .FirstOrDefaultAsync();

        if (roleCode is null)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        if (!await HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MembersRolesManage))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.MembersRolesManage));
        }
    }

    /// <summary> Возвращает название роли по коду. </summary>
    private static string GetRoleName(string roleCode) =>
        roleCode switch
        {
            OrganizationRoleCodes.Owner => "Владелец",
            OrganizationRoleCodes.Admin => "Администратор",
            OrganizationRoleCodes.Employee => "Сотрудник",
            OrganizationRoleCodes.Performer => "Исполнитель",
            OrganizationRoleCodes.Viewer => "Только просмотр",
            _ => roleCode
        };

    /// <summary> Нормализует код роли. </summary>
    private static string NormalizeRoleCode(string? roleCode) =>
        string.IsNullOrWhiteSpace(roleCode)
            ? string.Empty
            : roleCode.Trim().ToLowerInvariant();

    /// <summary> Нормализует код разрешения. </summary>
    private static string NormalizePermissionCode(string? permissionCode) =>
        string.IsNullOrWhiteSpace(permissionCode)
            ? string.Empty
            : permissionCode.Trim().ToLowerInvariant();
}
