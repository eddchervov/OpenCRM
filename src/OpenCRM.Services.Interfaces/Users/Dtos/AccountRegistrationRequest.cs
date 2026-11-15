using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> Запрос регистрации учётной записи. </summary>
public sealed record AccountRegistrationRequest(
    string Email,
    string? Password,
    bool PersonalDataProcessingConsent,
    bool AdvertisingConsent,
    RegistrationAttributionData? Attribution);
