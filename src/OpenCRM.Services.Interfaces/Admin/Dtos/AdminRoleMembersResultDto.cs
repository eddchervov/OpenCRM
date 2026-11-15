using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат получения участников роли. </summary>
public sealed class AdminRoleMembersResultDto
{
    /// <summary> Признак того, что роль не найдена. </summary>
    public bool NotFound { get; init; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Идентификатор роли. </summary>
    public string RoleId { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary> Признак защищённой роли. </summary>
    public bool IsProtected { get; init; }

    /// <summary> Список пользователей роли. </summary>
    public IReadOnlyList<AdminRoleMemberDto> Users { get; init; } = [];
}

