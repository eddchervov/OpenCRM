namespace OpenCRM.Domain.Enums;

/// <summary> Тип техники. </summary>
public enum VehicleKind
{
    /// <summary> Легковой автомобиль. </summary>
    PassengerCar = 1,

    /// <summary> Грузовая техника. </summary>
    Truck = 2,

    /// <summary> Мотоцикл. </summary>
    Motorcycle = 3,

    /// <summary> Квадроцикл. </summary>
    ATV = 4,

    /// <summary> Багги или UTV. </summary>
    UTV = 5,

    /// <summary> Снегоход. </summary>
    Snowmobile = 6,

    /// <summary> Гидроцикл. </summary>
    JetSki = 7,

    /// <summary> Лодка или катер. </summary>
    Boat = 8,

    /// <summary> Спецтехника. </summary>
    SpecialEquipment = 9,

    /// <summary> Прицеп. </summary>
    Trailer = 10,

    /// <summary> Другая техника. </summary>
    Other = 99
}
