using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Запрос обновления токенов через API. </summary>
public sealed class ApiRefreshTokenRequest
{
    /// <summary> Идентификатор пользователя. </summary>
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary> Refresh-токен для обновления. </summary>
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
