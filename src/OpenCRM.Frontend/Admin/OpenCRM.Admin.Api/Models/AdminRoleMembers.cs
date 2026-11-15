namespace OpenCRM.Admin.Api.Models;

/// <summary> Пользователи роли и доступные для назначения пользователи. </summary>
public sealed class AdminRoleMembers
{
    /// <summary> Идентификатор роли. </summary>
    public string RoleId { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Признак защищённой роли. </summary>
    public bool IsProtected { get; init; }

    /// <summary> Участники и доступные пользователи. </summary>
    public IReadOnlyList<AdminRoleMember> Users { get; init; } = [];
}
