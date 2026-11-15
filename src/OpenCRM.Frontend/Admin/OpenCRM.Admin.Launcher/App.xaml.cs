using System.Diagnostics;
using System.IO;
using System.Windows;
using Helpers;

namespace OpenCRM.Admin.Launcher;

/// <summary> Класс приложения лаунчера. </summary>
public partial class App
{
    /// <summary> Настраивает и запускает рабочее приложение при старте. </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var displayName = ProjectBrand.DefaultName;

        try
        {
            var settings = DesktopSettings.Load();
            displayName = settings.DisplayName;
            var chromePath = ChromeLocator.FindExecutable();
            var profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Администрирование",
                "AdminDesktop",
                "Chrome");

            Directory.CreateDirectory(profileDirectory);

            var startInfo = new ProcessStartInfo(chromePath)
            {
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add($"--app={CreateDesktopAddress(settings.AdminPanelUri)}");
            startInfo.ArgumentList.Add($"--user-data-dir={profileDirectory}");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--disable-default-apps");
            startInfo.ArgumentList.Add("--disable-quic");
            startInfo.ArgumentList.Add("--disable-http2");

            var chromeProcess = Process.Start(startInfo);
            if (chromeProcess == null)
            {
                throw new InvalidOperationException("Не удалось запустить Google Chrome.");
            }

            ChromeWindowActivator.Activate(chromeProcess, settings.DisplayName);
            DesktopLog.Write($"Административная панель открыта через Chrome: {settings.AdminPanelUri}");
        }
        catch (Exception exception)
        {
            DesktopLog.Write($"Ошибка запуска административной панели: {exception}");
            MessageBox.Show(
                $"Не удалось запустить административную панель.\n\n{exception.Message}",
                displayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        this.Shutdown();
    }

    /// <summary> Формирует адрес для десктопного приложения. </summary>
    private static string CreateDesktopAddress(Uri adminPanelUri)
    {
        var address = new UriBuilder(adminPanelUri);
        var existingQuery = address.Query.TrimStart('?');
        address.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? "desktop=1"
            : $"{existingQuery}&desktop=1";

        return address.Uri.AbsoluteUri;
    }
}
