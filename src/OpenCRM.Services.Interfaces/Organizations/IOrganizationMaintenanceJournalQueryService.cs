using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает отфильтрованный журнал обслуживаний организации.
/// </summary>
public interface IOrganizationMaintenanceJournalQueryService
{
    /// <summary>
    /// Возвращает страницу обслуживаний с фильтрами по статусу, периоду, выдаче и тексту.
    /// </summary>
    Task<OrganizationMaintenanceJournalResult> GetMaintenanceJournalAsync(
        Guid organizationId,
        MaintenanceStatus? status,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        int page,
        int pageSize,
        bool canViewClients,
        CancellationToken cancellationToken = default,
        string? handover = null,
        Guid? employeeId = null,
        bool canViewPayments = false);
}
