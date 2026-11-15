using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Возвращает сводные данные по клиентам обслуживаний. </summary>
internal sealed class MaintenanceClientSummaryQueryService(OpenCRMContext context) : IMaintenanceClientSummaryQueryService
{
    /// <summary> Контекст базы данных для сводных данных. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает сводные данные по обслуживанию для указанных клиентов. </summary>
    public async Task<IReadOnlyDictionary<Guid, MaintenanceClientSummaryDto>> GetByClientIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds)
    {
        if (clientIds.Count == 0)
        {
            return new Dictionary<Guid, MaintenanceClientSummaryDto>();
        }

        var summaries = await this._context.Maintenance
            .AsNoTracking()
            .Where(x => !x.IsDeleted && !x.Vehicle.IsDeleted && x.Vehicle.OrganizationId == organizationId)
            .Select(x => new
            {
                ClientId = x.BuyerClientId,
                x.Status,
                x.CompletedDate,
                x.Date
            })
            .Where(x => x.ClientId.HasValue && clientIds.Contains(x.ClientId.Value))
            .GroupBy(x => x.ClientId!.Value)
            .Select(group => new
            {
                ClientId = group.Key,
                Count = group.Count(),
                LastDate = group.Max(x => x.Status == MaintenanceStatus.Completed
                    ? x.CompletedDate ?? (DateTime?)x.Date
                    : null)
            })
            .ToDictionaryAsync(x => x.ClientId, x => new MaintenanceClientSummaryDto
            {
                Count = x.Count,
                LastDate = x.LastDate
            });

        return summaries;
    }

    /// <summary> Возвращает количество клиентов, связанных с обслуживаниями организации. </summary>
    public Task<int> GetLinkedCountAsync(Guid organizationId) => this._context.Maintenance
        .AsNoTracking()
        .Where(x => !x.IsDeleted
            && !x.Vehicle.IsDeleted
            && x.Vehicle.OrganizationId == organizationId
            && x.BuyerClientId.HasValue)
        .CountAsync();
}
