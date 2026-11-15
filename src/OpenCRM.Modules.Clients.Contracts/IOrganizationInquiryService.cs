using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Запросы и команды ручных обращений организации. </summary>
public interface IOrganizationInquiryService
{
    Task<OrganizationInquiryDto?> GetAsync(string userId, Guid organizationId, Guid inquiryId, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryPageDto> GetPageAsync(string userId, Guid organizationId, OrganizationInquiryQueryDto query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationInquiryDto>> GetActionRemindersAsync(string userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryBoardDto> GetBoardAsync(string userId, Guid organizationId, OrganizationInquiryQueryDto query, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryHistoryPageDto> GetHistoryAsync(string userId, Guid organizationId, Guid inquiryId, int page = 1, int pageSize = 30, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> CreateAsync(string userId, Guid organizationId, OrganizationInquirySaveDto dto, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> UpdateAsync(string userId, Guid organizationId, Guid inquiryId, OrganizationInquirySaveDto dto, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> ChangeStageAsync(string userId, Guid organizationId, Guid inquiryId, string stageCode, long version, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> ChangeStageAsync(string userId, Guid organizationId, Guid inquiryId, string stageCode, long version, string? comment, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> CompleteActionAsync(string userId, Guid organizationId, Guid inquiryId, long version, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> RescheduleActionAsync(string userId, Guid organizationId, Guid inquiryId, DateTime nextActionAt, long version, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> CloseAsync(string userId, Guid organizationId, Guid inquiryId, string reason, long version, CancellationToken cancellationToken = default);
    Task<OrganizationInquiryDto> AddNoteAsync(string userId, Guid organizationId, Guid inquiryId, string note, long version, CancellationToken cancellationToken = default);
}
