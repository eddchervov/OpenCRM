using Microsoft.Extensions.Configuration;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

internal sealed class AssistantTelegramConfiguration(IConfiguration configuration)
{
    private const string TelegramApiHost = "api.telegram.org";
    private const string TelegramLinkHost = "t.me";
    private readonly AssistantTelegramOptions _options = configuration.GetSection("AssistantTelegram").Get<AssistantTelegramOptions>()
        ?? new AssistantTelegramOptions();

    public string? BotToken => this.GetSecret("BotToken");

    public string? WebhookSecret => this.GetSecret("WebhookSecret");

    public string? BotUsername => this._options.BotUsername?.Trim().TrimStart('@');

    public int LinkTtlMinutes => this._options.LinkTtlMinutes;

    public int CallbackTtlMinutes => this._options.CallbackTtlMinutes;

    public int PollingIntervalSeconds => this._options.PollingIntervalSeconds;

    public int HttpTimeoutSeconds => this._options.HttpTimeoutSeconds;

    public int MaximumPayloadBytes => this._options.MaximumPayloadBytes;

    public int MaximumTextCharacters => this._options.MaximumTextCharacters;

    public int MaximumChoicesPerPage => this._options.MaximumChoicesPerPage;

    public int MaximumPendingPerUser => this._options.MaximumPendingPerUser;

    public bool LongPollingEnabled => this._options.LongPollingEnabled;

    public int LongPollingTimeoutSeconds => this._options.LongPollingTimeoutSeconds;

    public int LongPollingBatchSize => this._options.LongPollingBatchSize;

    public int MaximumAudioBytes => configuration.GetValue<int>("Assistant:MaximumAudioBytes");

    public int MaximumAudioDurationSeconds => configuration.GetValue<int>("Assistant:MaximumAudioDurationSeconds");

    public Uri ApiBaseUri => Uri.TryCreate(this._options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ? uri
        : throw new InvalidOperationException("В AssistantTelegram:ApiBaseUrl задан некорректный адрес.");

    public Uri BotLinkBaseUri => Uri.TryCreate(this._options.BotLinkBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ? uri
        : throw new InvalidOperationException("В AssistantTelegram:BotLinkBaseUrl задан некорректный адрес.");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(this.BotToken)
        && this.WebhookSecret is { Length: > 0 and <= 256 } webhookSecret
        && webhookSecret.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
        && this.BotUsername is { Length: >= 5 and <= 64 } username
        && username.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
        && this._options.LinkTtlMinutes > 0
        && this._options.CallbackTtlMinutes > 0
        && this._options.PollingIntervalSeconds > 0
        && this._options.HttpTimeoutSeconds > 0
        && this._options.MaximumPayloadBytes > 0
        && this._options.MaximumTextCharacters > 0
        && this._options.MaximumChoicesPerPage > 0
        && this._options.MaximumPendingPerUser > 0
        && this.MaximumAudioBytes > 0
        && this.MaximumAudioDurationSeconds > 0
        && IsSecureBaseUrl(this._options.ApiBaseUrl, TelegramApiHost)
        && IsSecureBaseUrl(this._options.BotLinkBaseUrl, TelegramLinkHost);

    public bool MatchesWebhookSecret(string? supplied)
    {
        return AssistantTelegramSecurity.MatchesWebhookSecret(this.WebhookSecret, supplied);
    }

    private string? GetSecret(string name)
    {
        var configured = configuration[$"AssistantTelegram:{name}"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var path = configuration[$"AssistantTelegram:{name}File"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSecureBaseUrl(string value, string host) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
