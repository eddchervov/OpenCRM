using System.IO;
using System.Text.Json;
using Helpers;

namespace OpenCRM.Admin.Launcher;

/// <summary> Настройки десктопного лаунчера. </summary>
internal sealed class DesktopSettings
{
    private readonly ProjectBrand _brand;

    /// <summary> Создаёт настройки с общими параметрами бренда. </summary>
    private DesktopSettings(ProjectBrand brand)
    {
        this._brand = brand;
    }

    /// <summary> Отображаемое имя приложения из бренда. </summary>
    public string DisplayName => this._brand.DisplayName;

    /// <summary> Адрес админ-панели в виде Uri. </summary>
    public Uri AdminPanelUri => CreateAdminPanelUri(
        $"{(Environment.GetEnvironmentVariable("OPENCRM_ADMIN_API_URL") ?? this._brand.PublicBaseUrl).TrimEnd('/')}/admin-panel/");

    /// <summary> Загружает настройки с диска. </summary>
    public static DesktopSettings Load()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "brand.json");
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var brand = ProjectBrand.Read(setting =>
        {
            var key = setting[(setting.LastIndexOf(':') + 1)..];
            return document.RootElement.TryGetProperty("Brand", out var brandSettings) && brandSettings.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;
        });
        var settings = new DesktopSettings(brand);
        _ = settings.AdminPanelUri;

        return settings;
    }

    /// <summary> Преобразует строку в Uri админ-панели. </summary>
    private static Uri CreateAdminPanelUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidDataException("Адрес админ-панели должен быть абсолютным HTTP- или HTTPS-адресом.");
        }

        return uri;
    }
}
