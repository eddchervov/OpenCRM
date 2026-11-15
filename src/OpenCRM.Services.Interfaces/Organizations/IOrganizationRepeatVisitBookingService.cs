using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Атомарное создание записи по необязательной рекомендации. </summary>
public interface IOrganizationRepeatVisitBookingService
{
    Task<OrganizationInquiryBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        Guid repeatVisitId,
        CreateOrganizationRepeatVisitBookingDto dto);
}
