using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса быстрой записи на детейлинг. </summary>
public interface IOrganizationDetailingQuickBookingService
{
    /// <summary> Создаёт быструю запись на детейлинг от имени пользователя. </summary>
    Task<OrganizationDetailingQuickBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationDetailingQuickBookingDto dto);
}
