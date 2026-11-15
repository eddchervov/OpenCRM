using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Contracts;

/// <summary> Контракт управления элементами справочника каталога. </summary>
public interface ICatalogCommandService
{
    /// <summary> Создаёт элемент справочника организации. </summary>
    Task<OrganizationScenarioCatalogItemDto> CreateAsync(string userId, Guid organizationId, CreateOrganizationScenarioCatalogItemDto dto);

    /// <summary> Изменяет элемент справочника организации. </summary>
    Task<OrganizationScenarioCatalogItemDto> UpdateAsync(string userId, Guid organizationId, Guid itemId, UpdateOrganizationScenarioCatalogItemDto dto);

    /// <summary> Удаляет элемент справочника организации. </summary>
    Task DeleteAsync(string userId, Guid organizationId, Guid itemId);
}
