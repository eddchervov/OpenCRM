using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO роли в админ-панели. </summary>
public sealed class AdminRoleDto
{
    /// <summary> Идентификатор роли. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Нормализованное название роли. </summary>
    public string? NormalizedName { get; init; }

    /// <summary> Количество пользователей с ролью. </summary>
    public int UsersCount { get; init; }

    /// <summary> Признак защищённой роли. </summary>
    public bool IsProtected { get; init; }

    /// <summary> Предварительный список пользователей роли. </summary>
    public IReadOnlyList<string> UsersPreview { get; init; } = [];
}

