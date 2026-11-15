using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Конвертация обращения организации в плановую запись. </summary>
public interface IOrganizationInquiryBookingService
{
    /// <summary> Создаёт плановую запись и фиксирует её связь с обращением. </summary>
    Task<OrganizationInquiryBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        CreateOrganizationInquiryBookingDto dto);
}
