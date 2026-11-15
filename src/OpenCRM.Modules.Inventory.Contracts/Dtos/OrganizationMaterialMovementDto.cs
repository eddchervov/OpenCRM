namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Движение материала на складе организации для отображения истории.
/// </summary>
public sealed class OrganizationMaterialMovementDto
{
    /// <summary>Идентификатор движения склада.</summary>
    public Guid Id { get; init; }

    /// <summary>Идентификатор складской позиции.</summary>
    public Guid StockId { get; init; }

    /// <summary>Связанный элемент scenario-catalog материалов, если он есть.</summary>
    public Guid? CatalogItemId { get; init; }

    /// <summary>Обслуживание, которое стало источником списания.</summary>
    public Guid? MaintenanceId { get; init; }

    /// <summary> Идентификатор связанной операции. </summary>
    public Guid? OperationId { get; init; }

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid? EmployeeId { get; init; }

    /// <summary> Идентификатор участника. </summary>
    public Guid? MemberId { get; init; }

    /// <summary>Название материала на момент отображения движения.</summary>
    public string MaterialName { get; init; } = string.Empty;

    /// <summary>Код типа движения склада.</summary>
    public string TypeCode { get; init; } = string.Empty;

    /// <summary>Количество движения: приход положительный, списание отрицательное.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Остаток материала после этого движения.</summary>
    public decimal BalanceAfter { get; init; }

    /// <summary>Учетная себестоимость единицы в момент движения.</summary>
    public decimal UnitCost { get; init; }

    /// <summary>Учетная сумма движения.</summary>
    public decimal TotalCost { get; init; }

    /// <summary>Стоимость остатка после движения.</summary>
    public decimal CostBalanceAfter { get; init; }

    /// <summary>Тип источника движения: maintenance, manual, transfer, reservation или сценарный код.</summary>
    public string? SourceEntityType { get; init; }

    /// <summary>Идентификатор источника движения.</summary>
    public Guid? SourceEntityId { get; init; }

    /// <summary>Человекочитаемое название источника движения.</summary>
    public string? SourceTitle { get; init; }

    /// <summary>Ссылка внутри workspace на карточку источника, если она есть.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Идентификатор пользователя, который создал движение или действие-источник.</summary>
    public string? CreatedByUserId { get; init; }

    /// <summary>Отображаемое имя пользователя, который создал движение или действие-источник.</summary>
    public string? CreatedByDisplayName { get; init; }

    /// <summary>Единица измерения движения.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Комментарий к движению.</summary>
    public string? Comment { get; init; }

    /// <summary>Дата создания движения.</summary>
    public DateTime CreatedAt { get; init; }
}
