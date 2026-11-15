namespace Notifications.Infrastructure.Providers.SmsRu;

/// <summary>
/// Содержит параметры интеграции с API SMS.ru.
/// </summary>
public sealed class SmsRuOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "SmsRu";

    /// <summary>Базовый адрес API провайдера.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Тайм-аут запроса к провайдеру в секундах.</summary>
    public int RequestTimeoutSeconds { get; init; }

    /// <summary>Секретный ключ доступа к API SMS.ru.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Необязательное согласованное имя отправителя SMS.</summary>
    public string? From { get; init; }
}
