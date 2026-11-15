using Microsoft.AspNetCore.Authorization;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Требование авторизации с указанием кода права. </summary>
public sealed class AdminPermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    /// <summary> Код требуемого права. </summary>
    public string PermissionCode { get; } = permissionCode;
}
