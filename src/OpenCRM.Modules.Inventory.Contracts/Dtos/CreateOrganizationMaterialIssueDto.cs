namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Команда ручного списания материала со склада организации.
/// </summary>
public sealed class CreateOrganizationMaterialIssueDto
{
    /// <summary>Складская позиция, с которой списывается материал.</summary>
    public Guid StockId { get; init; }

    /// <summary>Количество списания. В DTO передается положительным числом.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Комментарий или причина списания.</summary>
    public string? Comment { get; init; }
}
