namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Ответ API с токенами и данными пользователя. </summary>
public sealed class ApiTokenResponse
{
    /// <summary> Токен доступа. </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary> Время истечения токена доступа. </summary>
    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    /// <summary> Refresh-токен. </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary> Время истечения refresh-токена. </summary>
    public DateTimeOffset RefreshTokenExpiresAt { get; init; }

    /// <summary> Тип токена. </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary> Данные пользователя, прошедшего аутентификацию. </summary>
    public ApiUserResponse User { get; init; } = new();
}
