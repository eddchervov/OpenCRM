namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Позиция настройки каталога услуг или рабочих постов. </summary>
public sealed class CatalogSetupItemDto
{
    /// <summary> Идентификатор позиции каталога. </summary>
    public Guid Id { get; init; }

    /// <summary> Название позиции каталога. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Цена за единицу позиции каталога. </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary> Длительность выполнения позиции в минутах. </summary>
    public int? DurationMinutes { get; init; }

    /// <summary> Признак активности позиции каталога. </summary>
    public bool IsActive { get; init; }
}
