namespace OpenCRM.Admin.Api.Models;

/// <summary> Ответ с токенами доступа и данными пользователя. </summary>
public sealed class ApiTokenResponse
{
    /// <summary> Токен доступа. </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary> Время окончания действия токена доступа. </summary>
    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    /// <summary> Токен обновления. </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary> Время окончания действия токена обновления. </summary>
    public DateTimeOffset RefreshTokenExpiresAt { get; init; }

    /// <summary> Тип токена. </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary> Данные пользователя в ответе токена. </summary>
    public ApiTokenUser User { get; init; } = new();
}
