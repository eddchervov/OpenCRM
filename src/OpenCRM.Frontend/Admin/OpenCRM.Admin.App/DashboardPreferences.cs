namespace OpenCRM.Admin.App;

/// <summary> Хранение настроек панели мониторинга. </summary>
internal static class DashboardPreferences
{
    /// <summary> Путь к файлу настроек. </summary>
    private static readonly string s_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCRM", "Admin", "dashboard-refresh.txt");

    /// <summary> Загружает интервал обновления панели в секундах. </summary>
    public static int LoadRefreshSeconds()
    {
        try
        {
            if (File.Exists(DashboardPreferences.s_path) && int.TryParse(File.ReadAllText(DashboardPreferences.s_path), out var seconds)
                && seconds is 5 or 10 or 30 or 60)
            {
                return seconds;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Если настройки недоступны, используется стандартный интервал обновления.
        }

        return 60;
    }

    /// <summary> Сохраняет интервал обновления панели в секундах. </summary>
    public static void SaveRefreshSeconds(int seconds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DashboardPreferences.s_path)!);
            File.WriteAllText(DashboardPreferences.s_path, seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // При запрете записи выбор действует до закрытия приложения.
        }
    }
}
