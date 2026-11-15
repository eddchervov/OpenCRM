using System.Text.Json.Serialization;

namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Команда ручной корректировки остатка материала.
/// </summary>
public sealed class AdjustOrganizationMaterialStockDto
{
    /// <summary>Дата изменения складской позиции, на которой основана корректировка.</summary>
    public DateTime? ExpectedUpdatedAt { get; init; }

    /// <summary>Новый фактический остаток.</summary>
    public decimal QuantityOnHand { get; init; }

    /// <summary>Новый минимальный остаток, если его нужно изменить.</summary>
    public decimal? MinQuantity { get; init; }

    /// <summary>Новая средняя себестоимость единицы, если ее нужно изменить.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? AverageUnitCost { get; init; }

    /// <summary>Причина или комментарий к корректировке.</summary>
    public string? Comment { get; init; }
}
