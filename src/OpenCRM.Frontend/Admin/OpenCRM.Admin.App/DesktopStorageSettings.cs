using System.Text.Json;

namespace OpenCRM.Admin.App;

/// <summary> Пути локального хранилища рабочего приложения. </summary>
public sealed record DesktopStorageSettings(
    string? PendingActionsDirectory,
    string? SessionDirectory,
    string? DashboardPreferencesFile = null)
{
    /// <summary> Загружает необязательные пути из файла бренда. </summary>
    public static DesktopStorageSettings Load(string brandFilePath)
    {
        if (!File.Exists(brandFilePath))
        {
            return new DesktopStorageSettings(null, null);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(brandFilePath));
        if (!document.RootElement.TryGetProperty("AdminDesktop", out var settings) || settings.ValueKind != JsonValueKind.Object)
        {
            return new DesktopStorageSettings(null, null);
        }

        return new DesktopStorageSettings(
            ReadAbsolutePath(settings, "PendingActionsDirectory"),
            ReadAbsolutePath(settings, "SessionDirectory"),
            ReadAbsolutePath(settings, "DashboardPreferencesFile"));
    }

    private static string? ReadAbsolutePath(JsonElement settings, string propertyName)
    {
        if (!settings.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var configuredPath = value.GetString();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!Path.IsPathFullyQualified(expandedPath))
        {
            throw new InvalidOperationException($"Путь AdminDesktop:{propertyName} должен быть абсолютным.");
        }

        return expandedPath;
    }
}
