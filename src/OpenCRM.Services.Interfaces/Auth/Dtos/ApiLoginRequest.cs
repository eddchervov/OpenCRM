using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Запрос входа через API. </summary>
public sealed class ApiLoginRequest
{
    /// <summary> Email для входа. </summary>
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <summary> Пароль для входа. </summary>
    [Required]
    public string Password { get; init; } = string.Empty;
}
