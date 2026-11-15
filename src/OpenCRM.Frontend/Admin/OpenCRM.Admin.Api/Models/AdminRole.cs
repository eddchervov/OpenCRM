namespace OpenCRM.Admin.Api.Models;

/// <summary> Роль административной панели. </summary>
public sealed class AdminRole
{
    /// <summary> Идентификатор роли. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Название роли. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Количество пользователей с ролью. </summary>
    public int UsersCount { get; init; }

    /// <summary> Признак защищённой роли. </summary>
    public bool IsProtected { get; init; }
}
