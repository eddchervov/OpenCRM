using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Contracts;

/// <summary> Контракт настройки элементов справочника каталога. </summary>
public interface ICatalogSetupService
{
    /// <summary> Сохраняет набор элементов справочника заданного вида. </summary>
    Task SaveItemsAsync(Guid organizationId, string kindCode, IReadOnlyList<CatalogSetupItemDto> items);
}
