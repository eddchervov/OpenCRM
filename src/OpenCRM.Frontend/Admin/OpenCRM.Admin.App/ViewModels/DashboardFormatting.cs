using System.Globalization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Утилиты форматирования значений дашборда. </summary>
internal static class DashboardFormatting
{
    /// <summary> Культура для форматирования чисел. </summary>
    private static readonly CultureInfo s_culture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary> Форматирует значение по указанному формату. </summary>
    public static string Value(double? value, string format = "count")
    {
        if (value == null)
        {
            return "—";
        }

        return format switch
        {
            "money" => value.Value.ToString("C", s_culture),
            "percent" => value.Value.ToString("0.##", s_culture) + " %",
            "duration" => $"{(int)value.Value / 60} мин {(int)value.Value % 60} сек",
            "decimal" => value.Value.ToString("0.##", s_culture),
            _ => value.Value.ToString("N0", s_culture)
        };
    }
}
