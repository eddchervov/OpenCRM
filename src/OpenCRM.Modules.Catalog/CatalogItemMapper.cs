using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog;

/// <summary> Маппер элементов каталога в DTO. </summary>
internal static class CatalogItemMapper
{
    /// <summary> Преобразует элемент каталога в DTO с признаком использования. </summary>
    internal static OrganizationScenarioCatalogItemDto Map(OrganizationScenarioCatalogItem item, bool isUsed) => new()
    {
        Id = item.Id,
        ScenarioCode = item.ScenarioCode,
        KindCode = item.KindCode,
        Code = item.Code,
        Name = item.Name,
        Description = item.Description,
        UnitPrice = item.UnitPrice,
        CurrencyCode = item.CurrencyCode,
        DurationMinutes = item.DurationMinutes,
        ServiceIds = item.BundleItems.OrderBy(value => value.Position).Select(value => value.ServiceId).ToArray(),
        DocumentKindCode = item.DocumentKindCode,
        SortOrder = item.SortOrder,
        IsSystem = item.IsSystem,
        IsUsed = isUsed,
        IsActive = item.IsActive
    };
}
