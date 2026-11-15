using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Contracts;

/// <summary> Читает материал организации; права и границу транзакции задаёт вызывающий сценарий. </summary>
public interface ICatalogMaterialLookup
{
    /// <summary> Читает материал организации по идентификатору. </summary>
    Task<CatalogMaterialLookupDto?> GetMaterialAsync(Guid organizationId, Guid itemId, bool requireActive);
}
