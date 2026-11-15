using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

public interface IOrganizationRepeatVisitService
{
    Task<OrganizationRepeatVisitOptionsDto> GetOptionsAsync(string userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitPageDto> GetPageAsync(string userId, Guid organizationId, OrganizationRepeatVisitQueryDto query, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto?> GetAsync(string userId, Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto> CreateAsync(string userId, Guid organizationId, OrganizationRepeatVisitSaveDto dto, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto> UpdateAsync(string userId, Guid organizationId, Guid repeatVisitId, OrganizationRepeatVisitSaveDto dto, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto> CompleteActionAsync(string userId, Guid organizationId, Guid repeatVisitId, long version, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto> RescheduleActionAsync(string userId, Guid organizationId, Guid repeatVisitId, DateTime contactByUtc, long version, CancellationToken cancellationToken = default);
    Task<OrganizationRepeatVisitDto> ChangeStateAsync(string userId, Guid organizationId, Guid repeatVisitId, bool isActive, long version, CancellationToken cancellationToken = default);
}

public interface IOrganizationRepeatVisitBookingLinkService
{
    Task<OrganizationRepeatVisitBookingContext?> LockAndGetAsync(string userId, Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken = default);
    Task MarkBookedAsync(string userId, Guid organizationId, Guid repeatVisitId, Guid maintenanceId, long expectedVersion, CancellationToken cancellationToken = default);
}

public sealed class RepeatVisitConflictException(string message) : Exception(message);
