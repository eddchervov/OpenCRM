using System.Globalization;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Утилиты форматирования значений дашборда. </summary>
internal static class DashboardFormatting
{
    /// <summary> Форматирует значение по указанному формату. </summary>
    public static string Value(double? value, string format = "count")
    {
        if (value == null)
        {
            return "—";
        }

        return format switch
        {
            "money" => $"{value.Value.ToString("N2", CultureInfo.CurrentCulture)} RUB",
            "percent" => value.Value.ToString("0.##", CultureInfo.CurrentCulture) + " %",
            "duration" => string.Format(CultureInfo.CurrentCulture,
                AdminLocalization.Instance["core.dashboard.duration"], (int)value.Value / 60, (int)value.Value % 60),
            "decimal" => value.Value.ToString("0.##", CultureInfo.CurrentCulture),
            _ => value.Value.ToString("N0", CultureInfo.CurrentCulture)
        };
    }
}
