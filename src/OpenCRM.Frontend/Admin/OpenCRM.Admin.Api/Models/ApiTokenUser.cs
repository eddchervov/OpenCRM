namespace OpenCRM.Admin.Api.Models;

/// <summary> Данные пользователя в ответе с токенами. </summary>
public sealed class ApiTokenUser
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? Email { get; init; }

    /// <summary> Имя пользователя. </summary>
    public string? UserName { get; init; }

    /// <summary> Роли пользователя. </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
