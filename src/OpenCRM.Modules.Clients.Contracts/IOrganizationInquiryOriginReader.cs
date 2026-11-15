using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Читает источник записи обслуживания из доступного пользователю обращения. </summary>
public interface IOrganizationInquiryOriginReader
{
    Task<OrganizationInquiryOriginDto?> FindSourceAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        CancellationToken cancellationToken = default);
}
