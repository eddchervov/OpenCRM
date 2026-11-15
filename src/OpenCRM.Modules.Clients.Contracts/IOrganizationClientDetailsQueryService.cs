using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Контракт чтения подробных сведений о клиенте. </summary>
public interface IOrganizationClientDetailsQueryService
{
    /// <summary> Возвращает подробные сведения о клиенте. </summary>
    Task<OrganizationClientDetailsDto?> GetDetailsAsync(string userId, Guid organizationId, Guid clientId);
}
