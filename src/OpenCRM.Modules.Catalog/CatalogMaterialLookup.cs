using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog;

/// <summary> Сервис поиска материалов в каталоге организации. </summary>
internal sealed class CatalogMaterialLookup(OpenCRMContext context) : ICatalogMaterialLookup
{
    /// <summary> Возвращает материал каталога организации. </summary>
    public Task<CatalogMaterialLookupDto?> GetMaterialAsync(Guid organizationId, Guid itemId, bool requireActive) => context
        .OrganizationScenarioCatalogItems
        .AsNoTracking()
        .Where(item => item.OrganizationId == organizationId
            && item.Id == itemId
            && item.KindCode == OrganizationScenarioCatalogKindCodes.Material
            && (!requireActive || item.IsActive))
        .Select(item => new CatalogMaterialLookupDto(item.Id, item.Name))
        .FirstOrDefaultAsync();
}
