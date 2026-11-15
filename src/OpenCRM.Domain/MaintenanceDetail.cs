using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain;

/// <summary> Строка работы или услуги в записи об обслуживании. </summary>
public class MaintenanceDetail
{
    /// <summary> Идентификатор записи об обслуживании, к которой относится строка. </summary>
    public Guid MaintenanceId { get; set; }
    /// <summary> Идентификатор строки работы или услуги. </summary>
    public Guid DetailId { get; set; } = Guid.NewGuid();
    /// <summary> Порядковый номер строки в списке работ. </summary>
    public int Position { get; set; }
    /// <summary> Название работы или услуги. </summary>
    public string Name { get; set; } = default!;
    /// <summary> Описание работы или услуги. </summary>
    public string? Description { get; set; }
    /// <summary> Цена строки работы или услуги. </summary>
    public decimal? Price { get; set; }
    /// <summary> Код ставки НДС строки. </summary>
    public string? VatRateCode { get; set; }
    /// <summary> Идентификатор исполнителя строки. </summary>
    public Guid? EmployeeId { get; set; }
    /// <summary> Имя исполнителя строки. </summary>
    public string? EmployeeName { get; set; }
    /// <summary> Идентификатор позиции каталога, связанной со строкой. </summary>
    public Guid? CatalogItemId { get; set; }
    /// <summary> Количество единиц работы или услуги. </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Материал из scenario-catalog, если строка расходует складскую позицию.</summary>
    public Guid? MaterialCatalogItemId { get; set; }

    /// <summary>Название материала для ручной или справочной складской позиции.</summary>
    public string? MaterialName { get; set; }

    /// <summary>Количество материала, которое должно быть списано со склада по этой строке.</summary>
    public decimal? MaterialQuantity { get; set; }

    /// <summary>Единица измерения складского расхода.</summary>
    public string? MaterialUnit { get; set; }

    /// <summary>Себестоимость единицы материала для финансового расчета строки.</summary>
    public decimal? MaterialUnitCost { get; set; }

    /// <summary>Итоговая себестоимость материала по строке.</summary>
    public decimal? MaterialTotalCost { get; set; }
}

