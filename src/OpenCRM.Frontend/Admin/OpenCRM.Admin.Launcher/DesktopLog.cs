using System.Diagnostics;
using System.IO;
using System.Text;

namespace OpenCRM.Admin.Launcher;

/// <summary> Файловый лог лаунчера. </summary>
internal static class DesktopLog
{
    /// <summary> Объект для синхронизации записи лога. </summary>
    private static readonly object SyncRoot = new();

    /// <summary> Путь к файлу лога. </summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenCRM",
        "AdminDesktop",
        "Logs",
        "application.log");

    /// <summary> Дописывает сообщение в файл лога. </summary>
    public static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
    }
}
