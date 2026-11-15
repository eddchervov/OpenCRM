namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary> Складская позиция, доступная для операции. </summary>
public sealed class OperationMaterialStockDto
{
    /// <summary> Идентификатор позиции склада. </summary>
    public Guid Id { get; init; }

    /// <summary> Название материала. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Единица измерения материала. </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary> Количество в упаковке. </summary>
    public decimal? PackageQuantity { get; init; }

    /// <summary> Доступное количество на складе. </summary>
    public decimal AvailableQuantity { get; init; }
}
