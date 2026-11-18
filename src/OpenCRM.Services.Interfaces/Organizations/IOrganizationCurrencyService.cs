using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Управляет валютой денежных сумм организации. </summary>
public interface IOrganizationCurrencyService
{
    Task<CurrencySettingsDto> GetAsync(string userId, Guid organizationId);

    Task<CurrencySettingsDto> UpdateAsync(string userId, Guid organizationId, UpdateCurrencySettingsDto dto);
}
