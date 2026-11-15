namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary> Данные для отмены выдачи материала по операции. </summary>
public sealed class ReverseOperationMaterialDto
{
    /// <summary> Идентификатор команды отмены. </summary>
    public Guid CommandId { get; set; }

    /// <summary> Причина отмены выдачи. </summary>
    public string Reason { get; set; } = string.Empty;
}
