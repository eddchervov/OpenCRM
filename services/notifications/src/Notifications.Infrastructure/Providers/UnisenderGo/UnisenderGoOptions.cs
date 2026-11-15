namespace Notifications.Infrastructure.Providers.UnisenderGo;

/// <summary>Настройки отслеживания событий Unisender Go.</summary>
public sealed class UnisenderGoOptions
{
    /// <summary> Признак включения вебхуков Unisender Go. </summary>
    public bool WebhookEnabled { get; init; }

    /// <summary> Ключ проверки вебхуков. </summary>
    public string WebhookKey { get; init; } = string.Empty;



    /// <summary> Признак отслеживания переходов по ссылкам. </summary>
    public bool TrackLinks { get; init; }

    /// <summary> Признак отслеживания прочтения писем. </summary>
    public bool TrackRead { get; init; }
}
