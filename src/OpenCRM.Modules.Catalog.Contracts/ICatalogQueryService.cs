using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Contracts;

/// <summary> Контракт чтения каталога услуг и материалов организации. </summary>
public interface ICatalogQueryService
{
    /// <summary> Возвращает каталог организации для пользователя и организации. </summary>
    Task<OrganizationScenarioCatalogDto> GetAsync(string userId, Guid organizationId);

    /// <summary> Возвращает активную услугу каталога по её идентификатору. </summary>
    Task<OrganizationScenarioCatalogItemDto> GetActiveServiceAsync(string userId, Guid organizationId, Guid serviceId);

    /// <summary> Возвращает незавершённые записи, назначенные на рабочий пост. </summary>
    Task<WorkPostAffectedMaintenancesDto> GetAffectedWorkPostMaintenancesAsync(string userId, Guid organizationId, Guid workPostId);
}
