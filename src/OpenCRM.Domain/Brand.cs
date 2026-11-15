using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

/// <summary> Бренд техники в справочнике. </summary>
public class Brand
{
    /// <summary> Идентификатор записи. </summary>
    public int Id { get; set; }

    /// <summary> Название бренда. </summary>
    public string Name { get; set; } = default!;

    /// <summary> Ключ локального логотипа марки; отсутствие ключа скрывает логотип. </summary>
    public string? LogoKey { get; set; }

    /// <summary> Тип техники, к которому относится бренд. </summary>
    public VehicleKind Kind { get; set; } = VehicleKind.PassengerCar;

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }
}
