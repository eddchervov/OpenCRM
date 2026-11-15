using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает финансовую сводку рабочего пространства организации.
/// </summary>
public interface IOrganizationFinanceQueryService
{
    /// <summary>
    /// Возвращает финансовую сводку по обслуживаниям организации за период.
    /// </summary>
    Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo);
}
