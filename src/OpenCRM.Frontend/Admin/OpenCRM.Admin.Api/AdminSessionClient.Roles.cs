using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Получает роли административной панели. </summary>
    public Task<AdminRoles> GetAdminRolesAsync(CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<AdminRoles>(
            "api/v1/admin/roles",
            "Не удалось загрузить роли.",
            cancellationToken);
    }

    /// <summary> Получает разрешения административной панели. </summary>
    public Task<AdminPermissions> GetAdminPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<AdminPermissions>(
            "api/v1/admin/permissions",
            "Не удалось загрузить разрешения.",
            cancellationToken);
    }

    /// <summary> Получает участников роли и пользователей для назначения. </summary>
    public Task<AdminRoleMembers> GetAdminRoleMembersAsync(string roleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        return this.GetAuthorizedReportAsync<AdminRoleMembers>(
            $"api/v1/admin/roles/{Uri.EscapeDataString(roleId)}/members",
            "Не удалось загрузить участников роли.",
            cancellationToken);
    }

    /// <summary> Сохраняет разрешения роли. </summary>
    public Task<AdminOperationResponse> UpdateAdminRolePermissionsAsync(
        string roleId,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentNullException.ThrowIfNull(permissionCodes);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Put,
            $"api/v1/admin/permissions/roles/{Uri.EscapeDataString(roleId)}",
            new UpdateAdminRolePermissionsRequest(permissionCodes),
            "Не удалось сохранить права роли.",
            cancellationToken);
    }

    /// <summary> Назначает пользователя роли. </summary>
    public Task<AdminOperationResponse> AddAdminRoleMemberAsync(
        string roleId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/roles/{Uri.EscapeDataString(roleId)}/members",
            new UpdateAdminRoleMemberRequest(userId),
            "Не удалось назначить пользователя в роль.",
            cancellationToken);
    }

    /// <summary> Снимает роль с пользователя. </summary>
    public Task<AdminOperationResponse> RemoveAdminRoleMemberAsync(
        string roleId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Delete,
            $"api/v1/admin/roles/{Uri.EscapeDataString(roleId)}/members/{Uri.EscapeDataString(userId)}",
            null,
            "Не удалось снять роль с пользователя.",
            cancellationToken);
    }

    private sealed record UpdateAdminRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

    private sealed record UpdateAdminRoleMemberRequest(string UserId);
}
