using System.Globalization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Утилиты форматирования данных пользователя. </summary>
internal static class UserDisplayFormatting
{
    /// <summary> Культура для форматирования значений. </summary>
    private static readonly CultureInfo s_culture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary> Форматирует дату для отображения. </summary>
    public static string Date(DateTime? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("dd.MM.yyyy", s_culture);
    }

    /// <summary> Форматирует дату и время для отображения. </summary>
    public static string DateTime(DateTime? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", s_culture);
    }

    /// <summary> Форматирует дату и время со смещением. </summary>
    public static string DateTime(DateTimeOffset? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", s_culture);
    }

    /// <summary> Форматирует денежную сумму. </summary>
    public static string Money(decimal value) => value.ToString("C2", s_culture);

    /// <summary> Возвращает текст или заглушку для пустого значения. </summary>
    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
