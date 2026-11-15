namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Данные пользователя в ответе API. </summary>
public sealed class ApiUserResponse
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary> Email пользователя, если указан. </summary>
    public string? Email { get; init; }

    /// <summary> Имя пользователя, если задано. </summary>
    public string? UserName { get; init; }

    /// <summary> Роли пользователя. </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
