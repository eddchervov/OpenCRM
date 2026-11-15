namespace Notifications.Infrastructure;

/// <summary> Настраивает отдельный транспорт сообщений MAX для клиентских переписок. </summary>
public sealed class MaxChatOptions
{
    public const string SectionName = "MaxChat";

    public bool Enabled { get; init; }
    public string OpenCRMBaseUrl { get; init; } = string.Empty;
    public string ServiceKey { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 30;
}
