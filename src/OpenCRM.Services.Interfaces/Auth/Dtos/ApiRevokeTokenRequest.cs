using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Запрос отзыва refresh-токена через API. </summary>
public sealed class ApiRevokeTokenRequest
{
    /// <summary> Идентификатор пользователя. </summary>
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отзываемый refresh-токен. </summary>
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
