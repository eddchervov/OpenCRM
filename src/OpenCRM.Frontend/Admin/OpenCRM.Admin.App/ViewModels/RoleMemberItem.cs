using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Пользователь и состояние его участия в роли. </summary>
public sealed class RoleMemberItem(AdminRoleMember member)
{
    public string UserId { get; } = member.UserId;

    public string DisplayName { get; } = member.DisplayName;

    public string Email { get; } = member.Email ?? string.Empty;

    public string Phone { get; } = member.Phone ?? string.Empty;

    public bool HasRole { get; } = member.HasRole;

    public bool IsCurrentUser { get; } = member.IsCurrentUser;
}
