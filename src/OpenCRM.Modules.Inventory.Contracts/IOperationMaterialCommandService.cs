using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Контракт выдачи и отмены материалов по операциям. </summary>
public interface IOperationMaterialCommandService
{
    /// <summary> Выдаёт материал для строки операции обслуживания. </summary>
    Task<OperationMaterialIssueDto> IssueForOperationAsync(string userId, Guid organizationId, Guid maintenanceId, Guid detailId, IssueOperationMaterialDto request);

    /// <summary> Отменяет выдачу материала по операции. </summary>
    Task<OperationMaterialIssueDto> ReverseOperationMaterialAsync(string userId, Guid organizationId, Guid maintenanceId, Guid detailId, Guid movementId, ReverseOperationMaterialDto request);
}
