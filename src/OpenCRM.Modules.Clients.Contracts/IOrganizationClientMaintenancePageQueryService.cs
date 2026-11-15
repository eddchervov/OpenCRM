using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Чтение постраничной истории работ клиента. </summary>
public interface IOrganizationClientMaintenancePageQueryService
{
    /// <summary> Возвращает страницу работ с поиском и фильтром статуса либо null для отсутствующего клиента. </summary>
    Task<OrganizationClientMaintenancePageDto?> GetPageAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        int page = 1,
        int pageSize = 10,
        string? searchQuery = null,
        string? status = null);
}
