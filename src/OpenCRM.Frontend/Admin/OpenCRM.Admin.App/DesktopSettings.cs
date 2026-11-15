using System.Text.Json;
using Helpers;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App;

/// <summary> Настройки рабочего приложения. </summary>
public sealed class DesktopSettings
{
    /// <summary> Загружает базовый адрес API из общего файла бренда. </summary>
    public static ProjectBrand LoadBrand()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "brand.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return ProjectBrand.Read(setting =>
        {
            var key = setting[(setting.LastIndexOf(':') + 1)..];
            return document.RootElement.TryGetProperty("Brand", out var brand) && brand.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;
        });
    }

    /// <summary> Загружает дополнительные ссылки продукта из файла бренда. </summary>
    public static IReadOnlyList<AdminProductSection> LoadProductSections(string brandFilePath)
    {
        if (!File.Exists(brandFilePath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(brandFilePath));
        if (!document.RootElement.TryGetProperty("Administration", out var administration) ||
            !administration.TryGetProperty("Sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AdminProductSection>();
        foreach (var item in sections.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = ReadString(item, "Title");
            var url = ReadString(item, "Url");
            var permission = ReadString(item, "Permission");
            if (AdminProductSection.TryCreate(title, url, permission, out var section))
            {
                result.Add(section!);
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary> Загружает адрес API с учётом параметров бренда. </summary>
    public static Uri LoadApiAddress(ProjectBrand brand)
    {
        var address = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_API_URL") ?? brand.PublicBaseUrl;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Укажите HTTPS-адрес сервера в configuration/brand.json. HTTP допустим только для локальной разработки.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
