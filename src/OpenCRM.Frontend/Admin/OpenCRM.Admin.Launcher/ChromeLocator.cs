using System.IO;

namespace OpenCRM.Admin.Launcher;

/// <summary> Поиск исполняемого файла браузера Chrome. </summary>
internal static class ChromeLocator
{
    /// <summary> Находит путь к исполняемому файлу Chrome. </summary>
    public static string FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe")
        };

        var chromePath = candidates.FirstOrDefault(File.Exists);
        if (chromePath == null)
        {
            throw new FileNotFoundException("Google Chrome не установлен.");
        }

        return chromePath;
    }
}
