using System.Security.Cryptography;
using System.Text;
using Avalonia;

namespace OpenCRM.Admin.App;

/// <summary> Точка входа приложения. </summary>
internal static class Program
{
    /// <summary> Запускает приложение. </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            return;
        }

        var userIdentity = Environment.UserDomainName + "\\" + Environment.UserName;
        var instanceName = "Global\\OpenCRM.Admin." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity)));
        using var instance = new Mutex(true, instanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        try
        {
            Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    /// <summary> Строит конфигурацию приложения Avalonia. </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
