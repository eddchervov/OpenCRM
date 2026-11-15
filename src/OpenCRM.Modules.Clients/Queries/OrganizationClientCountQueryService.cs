using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Сервис запросов количества активных клиентов организации. </summary>
internal sealed class OrganizationClientCountQueryService(OpenCRMContext context) : IOrganizationClientCountQueryService
{
    /// <summary> Возвращает количество активных клиентов организации. </summary>
    public Task<int> GetActiveCountAsync(Guid organizationId) =>
        context.OrganizationClients.AsNoTracking()
            .CountAsync(client => client.OrganizationId == organizationId && !client.IsDeleted);
}
