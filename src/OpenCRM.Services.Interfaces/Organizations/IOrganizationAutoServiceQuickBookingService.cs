using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса быстрой записи в автосервис. </summary>
public interface IOrganizationAutoServiceQuickBookingService
{
    /// <summary> Создаёт быструю запись в автосервис от имени пользователя. </summary>
    Task<OrganizationAutoServiceQuickBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationAutoServiceQuickBookingDto dto);
}
