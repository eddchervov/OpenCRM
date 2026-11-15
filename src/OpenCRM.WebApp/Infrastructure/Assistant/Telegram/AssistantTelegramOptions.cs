namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

internal sealed class AssistantTelegramOptions
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string BotLinkBaseUrl { get; set; } = string.Empty;
    public string? BotUsername { get; set; }
    public int LinkTtlMinutes { get; set; }
    public int CallbackTtlMinutes { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public int HttpTimeoutSeconds { get; set; }
    public int MaximumPayloadBytes { get; set; }
    public int MaximumTextCharacters { get; set; }
    public int MaximumChoicesPerPage { get; set; }
    public int MaximumPendingPerUser { get; set; }
    public bool LongPollingEnabled { get; set; }
    public int LongPollingTimeoutSeconds { get; set; }
    public int LongPollingBatchSize { get; set; }
}
