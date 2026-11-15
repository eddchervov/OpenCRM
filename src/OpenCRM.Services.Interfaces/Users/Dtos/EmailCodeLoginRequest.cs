namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> Запрос входа по коду из письма. </summary>
public sealed record EmailCodeLoginRequest(
    string Email,
    string? Code,
    bool PersonalDataProcessingConsent,
    bool AdvertisingConsent);
