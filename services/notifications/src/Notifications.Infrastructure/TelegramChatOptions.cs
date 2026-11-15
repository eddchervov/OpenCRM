namespace Notifications.Infrastructure;

/// <summary>Настраивает отдельную очередь клиентских переписок Telegram.</summary>
public sealed class TelegramChatOptions
{
    public const string SectionName = "TelegramChat";

    public bool Enabled { get; init; }
    public string OpenCRMBaseUrl { get; init; } = string.Empty;
    public string ServiceKey { get; init; } = string.Empty;
    public string RelayBaseUrl { get; init; } = string.Empty;
    public string RelayKey { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public ushort PrefetchCount { get; init; } = 8;
    public int ConcurrencyLimit { get; init; } = 4;
    public int QueueMaxLength { get; init; } = 500;
    public int QueueMaxBytes { get; init; } = 8 * 1024 * 1024;
    public int InboxWorkerConcurrency { get; init; } = 4;
    public int InboxPollIntervalSeconds { get; init; } = 1;
    public int InboxLeaseSeconds { get; init; } = 300;
}
