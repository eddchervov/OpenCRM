using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenCRM.Admin.Launcher;

/// <summary> Активация и вывод окна Chrome на передний план. </summary>
internal static class ChromeWindowActivator
{
    /// <summary> Команда восстановления окна. </summary>
    private const int RestoreWindowCommand = 9;

    /// <summary> Максимальное время поиска окна приложения. </summary>
    private static readonly TimeSpan WindowSearchTimeout = TimeSpan.FromSeconds(12);

    /// <summary> Активирует окно запущенного процесса. </summary>
    public static void Activate(Process launchedProcess, string displayName)
    {
        var startedAt = DateTime.UtcNow;

        while (DateTime.UtcNow - startedAt < WindowSearchTimeout)
        {
            var windowHandle = FindWindow(launchedProcess, displayName);
            if (windowHandle != IntPtr.Zero)
            {
                ChromeWindowBranding.Apply(windowHandle, displayName);
                ShowWindowAsync(windowHandle, RestoreWindowCommand);
                SetForegroundWindow(windowHandle);
                return;
            }

            Thread.Sleep(200);
        }

        DesktopLog.Write("Окно Chrome создано, но его не удалось вывести на передний план.");
    }

    /// <summary> Находит дескриптор окна процесса приложения. </summary>
    private static IntPtr FindWindow(Process launchedProcess, string displayName)
    {
        launchedProcess.Refresh();
        if (!launchedProcess.HasExited && launchedProcess.MainWindowHandle != IntPtr.Zero)
        {
            return launchedProcess.MainWindowHandle;
        }

        var chromeProcesses = Process.GetProcessesByName("chrome");

        foreach (var process in chromeProcesses)
        {
            using (process)
            {
                if (process.MainWindowHandle == IntPtr.Zero
                    || !IsProjectWindow(process.MainWindowTitle, displayName))
                {
                    continue;
                }

                return process.MainWindowHandle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary> Проверяет, относится ли заголовок к окну проекта или администрирования. </summary>
    private static bool IsProjectWindow(string title, string displayName)
    {
        return title.Contains(displayName, StringComparison.OrdinalIgnoreCase)
            || title.Contains("Администрирование", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Administration", StringComparison.OrdinalIgnoreCase)
            || title.Contains("管理", StringComparison.Ordinal);
    }

    /// <summary> WinAPI-функция асинхронного показа окна с командой. </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    /// <summary> WinAPI-функция вывода окна на передний план. </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
