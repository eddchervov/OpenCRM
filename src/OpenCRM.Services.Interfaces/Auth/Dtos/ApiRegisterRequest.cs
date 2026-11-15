using System.ComponentModel.DataAnnotations;

using System.Text.Json.Serialization;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Auth.Dtos;

/// <summary> Запрос регистрации через API. </summary>
public sealed class ApiRegisterRequest
{
    /// <summary> Email для регистрации. </summary>
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <summary> Пароль для регистрации. </summary>
    [Required]
    public string Password { get; init; } = string.Empty;

    /// <summary> Признак согласия на обработку персональных данных. </summary>
    public bool PersonalDataProcessingConsent { get; init; }

    /// <summary> Признак согласия на рекламную рассылку. </summary>
    public bool AdvertisingConsent { get; init; }

    /// <summary> Данные атрибуции регистрации, если переданы. </summary>
    [JsonIgnore]
    public RegistrationAttributionData? Attribution { get; set; }
}
