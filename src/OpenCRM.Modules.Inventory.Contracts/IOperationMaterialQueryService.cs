using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Контракт чтения материалов, доступных для операций. </summary>
public interface IOperationMaterialQueryService
{
    /// <summary> Возвращает складские позиции, доступные для строки операции. </summary>
    Task<IReadOnlyList<OperationMaterialStockDto>> GetAvailableForOperationAsync(string userId, Guid organizationId, Guid maintenanceId, Guid detailId);

    /// <summary> Возвращает материалы, выданные для указанной работы обслуживания. </summary>
    Task<IReadOnlyList<OperationMaterialIssueDto>> GetIssuedForOperationAsync(string userId, Guid organizationId, Guid maintenanceId, Guid detailId);
}
