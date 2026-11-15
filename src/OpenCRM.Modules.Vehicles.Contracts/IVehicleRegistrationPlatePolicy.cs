using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Нормализует и проверяет регистрационные номера техники. </summary>
public interface IVehicleRegistrationPlatePolicy
{
    /// <summary> Нормализует номер; соответствие виду техники проверяет метод Validate. </summary>
    string? Normalize(string? value, bool allowOtherFormat = false);

    /// <summary> Проверяет совпадение номера с шаблоном заданного вида техники. </summary>
    bool MatchesFormat(string? value, VehicleKind kind);

    /// <summary> Проверяет безопасные символы и применимый шаблон номера. </summary>
    void Validate(string? value, VehicleKind kind, bool allowOtherFormat = false);

    /// <summary> Возвращает ограниченный список шаблонов и буквенных соответствий для интерфейса. </summary>
    VehicleRegistrationPlateFormatsDto GetFormats();

    /// <summary> Возвращает ограниченный набор вариантов для поиска частичного номера без проверки формата. </summary>
    IReadOnlyList<string> GetSearchVariants(string? value);
}
