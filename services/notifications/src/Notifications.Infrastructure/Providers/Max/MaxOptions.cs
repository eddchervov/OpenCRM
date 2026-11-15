namespace Notifications.Infrastructure.Providers.Max;

/// <summary>Содержит настройки разрешения токенов ботов MAX и времени ожидания запросов.</summary>
public sealed class MaxOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "Max";

    /// <summary>Базовый HTTPS-адрес сервиса, предоставляющего токены ботов организации.</summary>
    public string CredentialBaseUrl { get; init; } = string.Empty;

    /// <summary>Ключ доступа к сервису разрешения токенов.</summary>
    public string CredentialKey { get; init; } = string.Empty;

    /// <summary>Тайм-аут запросов к сервисам MAX в секундах.</summary>
    public int RequestTimeoutSeconds { get; init; }
}
