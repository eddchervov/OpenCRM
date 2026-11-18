using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Управляет личной валютой пользователя. </summary>
public interface IUserCurrencyService
{
    Task<CurrencySettingsDto> GetAsync(string userId);

    Task<CurrencySettingsDto> UpdateAsync(string userId, UpdateCurrencySettingsDto dto);
}
