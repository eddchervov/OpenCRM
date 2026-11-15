namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary> Результат выдачи материала по операции. </summary>
public sealed class OperationMaterialIssueDto
{
    /// <summary> Идентификатор движения материала. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор позиции склада. </summary>
    public Guid StockId { get; init; }

    /// <summary> Название материала. </summary>
    public string MaterialName { get; init; } = string.Empty;

    /// <summary> Количество выданного материала. </summary>
    public decimal Quantity { get; init; }

    /// <summary> Единица измерения материала. </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary> Дата создания выдачи. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Признак возврата материала. </summary>
    public bool IsReturn { get; init; }

    /// <summary> Идентификатор исходного движения. </summary>
    public Guid? SourceMovementId { get; init; }
}
