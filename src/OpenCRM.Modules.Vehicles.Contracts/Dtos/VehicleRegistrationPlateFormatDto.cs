namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Формат регистрационного номера для выбранных видов техники. </summary>
public sealed class VehicleRegistrationPlateFormatDto
{
    /// <summary> Короткий код формата. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Идентификаторы видов техники, которым соответствует формат. </summary>
    public IReadOnlyList<int> Kinds { get; init; } = [];

    /// <summary> Позиционные маски: L обозначает букву, знак # обозначает цифру. </summary>
    public IReadOnlyList<string> Masks { get; init; } = [];

    /// <summary> Регулярное выражение формата. </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary> Пример регистрационного номера для интерфейса. </summary>
    public string Example { get; init; } = string.Empty;
}
