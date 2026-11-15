using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO участника роли. </summary>
public sealed class AdminRoleMemberDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? Phone { get; init; }

    /// <summary> Признак наличия роли у пользователя. </summary>
    public bool HasRole { get; init; }

    /// <summary> Признак текущего пользователя. </summary>
    public bool IsCurrentUser { get; init; }
}

