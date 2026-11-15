using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Контракт фиксации связи обращения с созданной в общей транзакции записью. </summary>
public interface IOrganizationInquiryBookingLinkService
{
    Task<OrganizationInquiryBookingLinkDto?> LockAndGetAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        CancellationToken cancellationToken = default);

    Task MarkBookedAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        long expectedVersion,
        Guid maintenanceId,
        CancellationToken cancellationToken = default);
}
