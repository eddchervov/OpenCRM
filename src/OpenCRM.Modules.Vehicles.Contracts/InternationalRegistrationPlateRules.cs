using System.Globalization;
using System.Text;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Проверяет иностранные номера без изменения букв и национального формата. </summary>
public static class InternationalRegistrationPlateRules
{
    /// <summary> Код явно неизвестной страны; пустое значение сохраняет прежние российские правила. </summary>
    public const string UnknownCountryCode = "ZZ";

    /// <summary> Проверяет код ISO страны или явный код неизвестной страны ZZ. </summary>
    public static string? NormalizeCountryCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var code = value.Trim().ToUpperInvariant();
        if (code == InternationalRegistrationPlateRules.UnknownCountryCode)
        {
            return code;
        }

        if (code.Length != 2 || !code.All(character => character is >= 'A' and <= 'Z'))
        {
            throw new InvalidOperationException("Выберите страну регистрации из списка.");
        }

        try
        {
            var region = new RegionInfo(code);
            if (region.TwoLetterISORegionName != code)
            {
                throw new InvalidOperationException("Выберите страну регистрации из списка.");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Страна регистрации не найдена.");
        }

        return code;
    }

    /// <summary> Сохраняет написание иностранного номера, проверяя длину и безопасные символы. </summary>
    public static string? NormalizePlate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var plate = value.Trim();
        if (plate.Length > 24)
        {
            throw new InvalidOperationException("Регистрационный номер не должен быть длиннее 24 символов.");
        }

        var letterOrDigitCount = 0;
        foreach (var character in plate.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(character))
            {
                letterOrDigitCount++;
                continue;
            }

            if (character.Value is ' ' or '-' or '·')
            {
                continue;
            }

            throw new InvalidOperationException("Регистрационный номер может содержать буквы, цифры, пробелы, дефисы и среднюю точку.");
        }

        if (letterOrDigitCount < 2)
        {
            throw new InvalidOperationException("Регистрационный номер должен содержать не менее двух букв или цифр.");
        }

        return plate;
    }
}
