namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Настройки форматов регистрационных номеров для интерфейса. </summary>
public sealed class VehicleRegistrationPlateFormatsDto
{
    /// <summary> Максимальная длина номера до нормализации. </summary>
    public int MaxLength { get; init; }

    /// <summary> Шаблон символа для буквы в масках. </summary>
    public string LetterPattern { get; init; } = string.Empty;

    /// <summary> Сопоставления латинских букв с кириллическими аналогами. </summary>
    public IReadOnlyDictionary<string, string> CharacterMappings { get; init; } = new Dictionary<string, string>();

    /// <summary> Строгие форматы для видов техники. </summary>
    public IReadOnlyList<VehicleRegistrationPlateFormatDto> Formats { get; init; } = [];
}
