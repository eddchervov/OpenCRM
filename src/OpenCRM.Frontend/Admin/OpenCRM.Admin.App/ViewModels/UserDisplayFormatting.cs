using System.Globalization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Утилиты форматирования данных пользователя. </summary>
internal static class UserDisplayFormatting
{
    /// <summary> Форматирует дату для отображения. </summary>
    public static string Date(DateTime? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary> Форматирует дату и время для отображения. </summary>
    public static string DateTime(DateTime? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary> Форматирует дату и время со смещением. </summary>
    public static string DateTime(DateTimeOffset? value)
    {
        if (value is null || value.Value.Year <= 1)
        {
            return "—";
        }

        return value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary> Форматирует календарную дату без сдвига по часовому поясу. </summary>
    public static string CalendarDate(DateTime? value) => value is null || value.Value.Year <= 1
        ? "—"
        : value.Value.ToString("d", CultureInfo.CurrentCulture);

    /// <summary> Форматирует сумму с её кодом валюты без пересчёта. </summary>
    public static string Money(decimal value, string currencyCode = "RUB") =>
        $"{value.ToString("N2", CultureInfo.CurrentCulture)} {currencyCode}";

    /// <summary> Возвращает текст или заглушку для пустого значения. </summary>
    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
