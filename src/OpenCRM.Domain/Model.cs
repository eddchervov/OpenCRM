using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

/// <summary> Модель техники в справочнике. </summary>
public class Model
{
    /// <summary> Идентификатор записи. </summary>
    public int Id { get; set; }

    /// <summary> Название модели. </summary>
    public string Name { get; set; } = default!;

    /// <summary> Идентификатор бренда. </summary>
    public int BrandId { get; set; }

    /// <summary> Бренд модели. </summary>
    public Brand Brand { get; set; } = default!;

    /// <summary> Тип техники, к которому относится модель. </summary>
    public VehicleKind Kind { get; set; } = VehicleKind.PassengerCar;

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }
}
