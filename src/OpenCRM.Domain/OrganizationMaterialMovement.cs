namespace OpenCRM.Domain;

/// <summary>
/// Движение материала на складе организации: приход, корректировка или списание.
/// </summary>
public class OrganizationMaterialMovement
{
    /// <summary>Идентификатор движения склада.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Идентификатор организации, в которой произошло движение.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Организация-владелец складского движения.</summary>
    public Organization Organization { get; set; } = null!;

    /// <summary>Идентификатор складской позиции, по которой прошло движение.</summary>
    public Guid StockId { get; set; }

    /// <summary>Складская позиция материала или запчасти.</summary>
    public OrganizationMaterialStock Stock { get; set; } = null!;

    /// <summary>Элемент scenario-catalog, если движение связано со справочником материалов.</summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary>Справочный материал из scenario-catalog.</summary>
    public OrganizationScenarioCatalogItem? CatalogItem { get; set; }

    /// <summary>Обслуживание, из которого автоматически создано списание.</summary>
    public Guid? MaintenanceId { get; set; }

    /// <summary>Обслуживание, связанное с движением склада.</summary>
    public Maintenance? Maintenance { get; set; }

    /// <summary>Операция, на которую исполнитель выдал материал.</summary>
    public Guid? OperationId { get; set; }

    /// <summary>Сотрудник, получивший материал для операции.</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>Участник организации, выполнивший выдачу.</summary>
    public Guid? MemberId { get; set; }

    /// <summary>Идентификатор команды для безопасного повтора выдачи.</summary>
    public Guid? CommandId { get; set; }

    /// <summary>Код типа движения: приход, списание, корректировка или списание по обслуживанию.</summary>
    public string TypeCode { get; set; } = OrganizationMaterialMovementTypeCodes.Receipt;

    /// <summary>Количество движения. Приход положительный, списание отрицательное.</summary>
    public decimal Quantity { get; set; }

    /// <summary> Введённое количество материала в движении. </summary>
    public decimal? EnteredQuantity { get; set; }

    /// <summary> Единица измерения введённого количества. </summary>
    public string? EnteredUnit { get; set; }

    /// <summary> Коэффициент пересчёта введённого количества в базовую единицу. </summary>
    public decimal? ConversionFactor { get; set; }

    /// <summary>Единица измерения движения.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Учетная себестоимость единицы в момент движения.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Учетная сумма движения. Для списаний хранится положительной суммой расхода.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Остаток количества после движения.</summary>
    public decimal QuantityBalanceAfter { get; set; }

    /// <summary>Стоимость остатка после движения.</summary>
    public decimal CostBalanceAfter { get; set; }

    /// <summary>Тип источника движения: maintenance, manual, transfer, reservation или другой сценарный код.</summary>
    public string? SourceEntityType { get; set; }

    /// <summary>Идентификатор источника движения, если он не покрывается специальным внешним ключом.</summary>
    public Guid? SourceEntityId { get; set; }

    /// <summary>Комментарий к приходу, корректировке или автоматическому списанию.</summary>
    public string? Comment { get; set; }

    /// <summary>Пользователь, который создал движение или действие-источник.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Дата создания движения.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
