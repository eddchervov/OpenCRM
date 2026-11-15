using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Контракт управления запасами материалов организации. </summary>
public interface IStockCommandService
{
    /// <summary> Оформляет поступление материала на склад. </summary>
    Task<OrganizationMaterialStockDto> AddReceiptAsync(string userId, Guid organizationId, CreateOrganizationMaterialReceiptDto request);

    /// <summary> Корректирует остаток материала на складе. </summary>
    Task<OrganizationMaterialStockDto> AdjustAsync(string userId, Guid organizationId, Guid stockId, AdjustOrganizationMaterialStockDto request);

    /// <summary> Списывает материал со склада, оформляя выдачу. </summary>
    Task<OrganizationMaterialStockDto> IssueAsync(string userId, Guid organizationId, CreateOrganizationMaterialIssueDto request);
}
