using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Контракт чтения складских данных организации. </summary>
public interface IInventoryQueryService
{
    /// <summary> Возвращает складские данные организации. </summary>
    Task<OrganizationMaterialInventoryDto> GetAsync(string userId, Guid organizationId);

    /// <summary> Возвращает складские данные с фильтрами и постранично. </summary>
    Task<OrganizationMaterialInventoryDto> GetAsync(string userId, Guid organizationId, string? searchQuery, string? stockFilter, string? movementType, int stocksPage, int stocksPageSize, int movementsPage, int movementsPageSize, DateTime? movementDateFrom = null, DateTime? movementDateTo = null, DateTime? snapshotAt = null, Guid? stockId = null);
}
