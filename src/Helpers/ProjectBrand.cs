namespace Helpers;

/// <summary> Название и публичные ссылки текущего экземпляра приложения. </summary>
#if BRANDING_INTERNAL
internal sealed class ProjectBrand
#else
public sealed class ProjectBrand
#endif
{
    /// <summary> Название, используемое без отдельной настройки. </summary>
    public const string DefaultName = "OpenCRM";

    /// <summary> Постоянная ссылка проекта на GitHub. </summary>
    public const string GitHubUrl = "https://github.com/eddchervov";

    /// <summary> Создаёт неизменяемые настройки бренда. </summary>
    public ProjectBrand(
        string displayName = DefaultName,
        string telegramUrl = "",
        string vkUrl = "",
        string instagramUrl = "",
        string publicBaseUrl = "https://localhost:7142")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
            || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException("Публичный адрес должен быть HTTPS-адресом; HTTP допустим только локально.", nameof(publicBaseUrl));
        }

        this.PublicBaseUrl = address.AbsoluteUri.TrimEnd('/');
        this.DisplayName = displayName.Trim();
        this.TelegramUrl = telegramUrl;
        this.VkUrl = vkUrl;
        this.InstagramUrl = instagramUrl;
    }

    /// <summary> Читает общие настройки бренда из конфигурации хоста. </summary>
    public static ProjectBrand Read(Func<string, string?> readSetting)
    {
        return new ProjectBrand(
            readSetting("Brand:DisplayName") ?? ProjectBrand.DefaultName,
            readSetting("Brand:TelegramUrl") ?? string.Empty,
            readSetting("Brand:VkUrl") ?? string.Empty,
            readSetting("Brand:InstagramUrl") ?? string.Empty,
            readSetting("Brand:PublicBaseUrl") ?? "https://localhost:7142");
    }

    /// <summary> Публичный адрес приложения с учётом базового пути. </summary>
    public string PublicBaseUrl { get; }

    /// <summary> Отображаемое название проекта. </summary>
    public string DisplayName { get; }

    /// <summary> Название для поисковых описаний в нижнем регистре. </summary>
    public string DisplayNameLower => this.DisplayName.ToLowerInvariant();

    /// <summary> Ссылка на Telegram, если сообщество настроено. </summary>
    public string TelegramUrl { get; }

    /// <summary> Ссылка на ВКонтакте, если сообщество настроено. </summary>
    public string VkUrl { get; }

    /// <summary> Ссылка на Instagram, если сообщество настроено. </summary>
    public string InstagramUrl { get; }
}
