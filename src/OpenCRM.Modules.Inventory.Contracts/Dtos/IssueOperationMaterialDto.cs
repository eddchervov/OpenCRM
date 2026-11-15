namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary> Материал для выдачи по операции обслуживания. </summary>
public sealed class IssueOperationMaterialDto
{
    /// <summary> Идентификатор позиции склада. </summary>
    public Guid StockId { get; set; }

    /// <summary> Количество выдаваемого материала. </summary>
    public decimal Quantity { get; set; }

    /// <summary> Единица измерения материала. </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary> Идентификатор команды выдачи. </summary>
    public Guid CommandId { get; set; }

    /// <summary> Комментарий к выдаче. </summary>
    public string? Comment { get; set; }
}
