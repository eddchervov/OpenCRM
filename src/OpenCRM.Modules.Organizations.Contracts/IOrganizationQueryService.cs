using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Contracts;

/// <summary> Контракт запросов организаций. </summary>
public interface IOrganizationQueryService
{
    /// <summary> Возвращает организацию по идентификатору. </summary>
    Task<OrganizationDto> GetByIdAsync(string userId, Guid organizationId);

    /// <summary> Возвращает обзор организаций пользователя. </summary>
    Task<TeamOverviewDto> GetOverviewAsync(string userId);
}
