using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Catalog;

/// <summary> Читатель признаков использования элементов каталога. </summary>
internal sealed class CatalogUsageReader(OpenCRMContext context)
{
    /// <summary> Возвращает идентификаторы когда-либо использованных элементов. </summary>
    internal async Task<HashSet<Guid>> GetUsedIdsAsync(Guid organizationId)
    {
        var ids = await this.GetUsedItems(organizationId).Select(item => item.Id).ToListAsync();

        return ids.ToHashSet();
    }

    /// <summary> Проверяет, использовался ли элемент каталога хотя бы однажды. </summary>
    internal Task<bool> IsUsedAsync(Guid organizationId, Guid itemId) =>
        this.GetUsedItems(organizationId).AnyAsync(item => item.Id == itemId);

    /// <summary> Формирует запрос элементов с сохранённым признаком первого использования. </summary>
    private IQueryable<OrganizationScenarioCatalogItem> GetUsedItems(Guid organizationId) =>
        context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.HasBeenUsed);
}
