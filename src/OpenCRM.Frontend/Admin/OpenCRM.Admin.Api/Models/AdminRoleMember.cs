namespace OpenCRM.Admin.Api.Models;

/// <summary> Пользователь, которого можно включить в роль. </summary>
public sealed class AdminRoleMember
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Признак наличия роли. </summary>
    public bool HasRole { get; init; }

    /// <summary> Признак текущего пользователя. </summary>
    public bool IsCurrentUser { get; init; }
}
