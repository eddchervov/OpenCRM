namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для создания строки работы или услуги обслуживания. </summary>
public class CreateMaintenanceDetailDto
{
    /// <summary> Идентификатор строки работы, если задаётся. </summary>
    public Guid? DetailId { get; set; }

    /// <summary> Наименование работы или услуги. </summary>
    public required string Name { get; set; } = default!;

    /// <summary> Описание работы или услуги. </summary>
    public string? Description { get; set; }

    /// <summary> Цена работы или услуги. </summary>
    public decimal? Price { get; set; }

    /// <summary> Код ставки НДС. </summary>
    public string? VatRateCode { get; set; }

    /// <summary> Идентификатор сотрудника-исполнителя. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Имя сотрудника-исполнителя. </summary>
    public string? EmployeeName { get; set; }

    /// <summary> Идентификатор позиции каталога для услуги. </summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary> Количество услуги. </summary>
    public int Quantity { get; set; } = 1;

    /// <summary> Идентификатор позиции каталога материала. </summary>
    public Guid? MaterialCatalogItemId { get; set; }

    /// <summary> Наименование материала. </summary>
    public string? MaterialName { get; set; }

    /// <summary> Количество материала. </summary>
    public decimal? MaterialQuantity { get; set; }

    /// <summary> Единица измерения материала. </summary>
    public string? MaterialUnit { get; set; }

    /// <summary> Стоимость единицы материала. </summary>
    public decimal? MaterialUnitCost { get; set; }

    /// <summary> Общая стоимость материала. </summary>
    public decimal? MaterialTotalCost { get; set; }
}

