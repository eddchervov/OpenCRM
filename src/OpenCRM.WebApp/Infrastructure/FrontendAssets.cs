using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCRM.WebApp.Infrastructure;

/// <summary> Чтение манифеста сборки фронтенда для подключения ресурсов. </summary>
public static class FrontendAssets
{
    /// <summary> Настройки JSON-сериализации для чтения манифеста. </summary>
    private static readonly JsonSerializerOptions s_serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary> Читает из манифеста пути скрипта и стилей точки входа. </summary>
    public static (string Script, IReadOnlyList<string> Stylesheets) ReadEntry(
        string webRootPath,
        string clientPath,
        string entryName)
    {
        var clientRoot = Path.GetFullPath(Path.Combine(webRootPath, clientPath));
        var manifestPath = Path.Combine(clientRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Не найден frontend manifest: {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(
            File.ReadAllText(manifestPath),
            s_serializerOptions);
        if (manifest is null || !manifest.TryGetValue(entryName, out var entry))
        {
            throw new InvalidOperationException($"В frontend manifest не найдена точка входа: {entryName}");
        }

        var script = ToPublicPath(clientRoot, clientPath, entry.File);
        var stylesheets = (entry.Css ?? []).Select(assetPath => ToPublicPath(clientRoot, clientPath, assetPath)).ToArray();

        return (script, stylesheets);
    }

    /// <summary> Преобразует путь ресурса в публичный URL. </summary>
    private static string ToPublicPath(string clientRoot, string clientPath, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)
            || Path.IsPathRooted(assetPath)
            || assetPath.Contains('\\')
            || assetPath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Недопустимый путь frontend-ресурса: {assetPath}");
        }

        var physicalPath = Path.GetFullPath(Path.Combine(clientRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        var clientRootPrefix = clientRoot.EndsWith(Path.DirectorySeparatorChar)
            ? clientRoot
            : clientRoot + Path.DirectorySeparatorChar;
        if (!physicalPath.StartsWith(clientRootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(physicalPath))
        {
            throw new InvalidOperationException($"Не найден frontend-ресурс: {physicalPath}");
        }

        return $"~/{clientPath.TrimEnd('/')}/{assetPath}";
    }

    /// <summary> Запись манифеста сборки фронтенда. </summary>
    private sealed class ManifestEntry
    {
        /// <summary> Путь к собранному файлу записи. </summary>
        [JsonPropertyName("file")]
        public string File { get; init; } = string.Empty;

        /// <summary> Пути к CSS-файлам записи. </summary>
        [JsonPropertyName("css")]
        public string[]? Css { get; init; }
    }
}
