namespace OpenCRM.Infrastructure.Auth;

/// <summary> Настройки выпуска JWT-токенов. </summary>
public sealed class JwtTokenOptions
{
    /// <summary> Издатель токена. </summary>
    public string Issuer { get; set; } = "OpenCRM";

    /// <summary> Аудитория токена. </summary>
    public string Audience { get; set; } = "OpenCRM.Api";

    /// <summary> Ключ подписи токенов. </summary>
    public string? SigningKey { get; set; }

    /// <summary> Срок жизни access-токена в минутах. </summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary> Срок жизни refresh-токена в днях. </summary>
    public int RefreshTokenDays { get; set; } = 30;
}
