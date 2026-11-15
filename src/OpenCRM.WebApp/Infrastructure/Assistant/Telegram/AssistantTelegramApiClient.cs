using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

public sealed class AssistantTelegramApiClient(
    IHttpClientFactory clientFactory,
    IConfiguration configuration) : IAssistantTelegramApiClient
{
    private const int TelegramMaximumMessageCharacters = 4096;
    private const int TelegramMaximumCallbackAnswerCharacters = 200;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly AssistantTelegramConfiguration _configuration = new(configuration);

    public async Task SendMessageAsync(
        long chatId,
        string text,
        IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>? buttons,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = Limit(text, Math.Min(this._configuration.MaximumTextCharacters, TelegramMaximumMessageCharacters))
        };
        if (buttons is { Count: > 0 })
        {
            payload["reply_markup"] = new { inline_keyboard = buttons };
        }

        await this.PostAsync("sendMessage", payload, cancellationToken);
    }

    public async Task SendVoiceAsync(long chatId, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var token = this.RequireBotToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, this.ApiUri($"bot{token}/sendVoice"));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "chat_id");
        var audio = new ByteArrayContent(content);
        audio.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        var extension = contentType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp4" => "m4a",
            _ => "ogg"
        };
        form.Add(audio, "voice", $"assistant.{extension}");
        request.Content = form;
        using var response = await this.CreateClient().SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        await this.PostAsync("answerCallbackQuery", new { callback_query_id = callbackQueryId, text = Limit(text, TelegramMaximumCallbackAnswerCharacters) }, cancellationToken);
    }

    public async Task<(byte[] Content, string ContentType)> DownloadVoiceAsync(string fileId, long maximumBytes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId) || maximumBytes <= 0)
        {
            throw new InvalidOperationException("Голосовое сообщение превышает допустимый размер.");
        }

        var token = this.RequireBotToken();
        using var metadataResponse = await this.CreateClient().GetAsync(
            this.ApiUri($"bot{token}/getFile?file_id={Uri.EscapeDataString(fileId)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var metadata = await ReadResultAsync<TelegramFileEnvelope>(metadataResponse, cancellationToken);
        var path = metadata.Result?.FilePath;
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Telegram вернул некорректный путь аудиофайла.");
        }

        var escapedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using var fileResponse = await this.CreateClient().GetAsync(
            this.ApiUri($"file/bot{token}/{escapedPath}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        fileResponse.EnsureSuccessStatusCode();
        if (fileResponse.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidOperationException("Голосовое сообщение превышает допустимый размер.");
        }

        await using var stream = await fileResponse.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidOperationException("Голосовое сообщение превышает допустимый размер.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var contentType = fileResponse.Content.Headers.ContentType?.MediaType ?? "audio/ogg";
        return (buffer.ToArray(), contentType);
    }

    public async Task ValidatePollingBotAsync(CancellationToken cancellationToken)
    {
        var identity = await this.PollingRequestAsync<JsonElement>("getMe", new { }, cancellationToken);
        if (!identity.TryGetProperty("username", out var username) ||
            !string.Equals(username.GetString(), this._configuration.BotUsername, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Токен Telegram не соответствует настроенному имени бота.");
        }

        var webhook = await this.PollingRequestAsync<JsonElement>("getWebhookInfo", new { }, cancellationToken);
        if (!webhook.TryGetProperty("url", out var url) || !string.IsNullOrEmpty(url.GetString()))
        {
            throw new InvalidOperationException("У бота уже установлен вебхук. Используйте отдельного тестового бота без вебхука.");
        }
    }

    public Task<JsonElement[]> GetUpdatesAsync(long? offset, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["timeout"] = this._configuration.LongPollingTimeoutSeconds,
            ["limit"] = this._configuration.LongPollingBatchSize,
            ["allowed_updates"] = new[] { "message", "callback_query" }
        };
        if (offset.HasValue)
        {
            payload["offset"] = offset.Value;
        }

        return this.PollingRequestAsync<JsonElement[]>("getUpdates", payload, cancellationToken);
    }

    private async Task<T> PollingRequestAsync<T>(string method, object payload, CancellationToken cancellationToken)
    {
        var token = this.RequireBotToken();
        using var request = new HttpRequestMessage(HttpMethod.Post, this.ApiUri($"bot{token}/{method}"))
        {
            Content = JsonContent.Create(payload, options: s_json)
        };
        using var response = await this.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var maximumBytes = checked((long)this._configuration.MaximumPayloadBytes * (this._configuration.LongPollingBatchSize + 1));
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidOperationException("Ответ Telegram превышает допустимый размер.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidOperationException("Ответ Telegram превышает допустимый размер.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<TelegramResultEnvelope<T>>(buffer, s_json, cancellationToken);
        if (!response.IsSuccessStatusCode || result?.Ok != true || result.Result is null)
        {
            throw new InvalidOperationException("Не удалось получить обновления Telegram. Проверьте токен и единственный запущенный экземпляр пилота.");
        }

        return result.Result;
    }

    private async Task PostAsync(string method, object payload, CancellationToken cancellationToken)
    {
        var token = this.RequireBotToken();
        using var response = await this.CreateClient().PostAsJsonAsync(
            this.ApiUri($"bot{token}/{method}"), payload, s_json, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private string RequireBotToken() => this._configuration.BotToken is { Length: > 0 } token
        ? token
        : throw new InvalidOperationException("Telegram-бот не настроен.");

    private HttpClient CreateClient()
    {
        var client = clientFactory.CreateClient("assistant-telegram");
        client.Timeout = TimeSpan.FromSeconds(this._configuration.HttpTimeoutSeconds);

        return client;
    }

    private Uri ApiUri(string path) => new(this._configuration.ApiBaseUri, "./" + path);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<TelegramResponse>(s_json, cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Ok != true)
        {
            throw new InvalidOperationException("Telegram не принял сообщение.");
        }
    }

    private static async Task<TelegramResultEnvelope<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<TelegramResultEnvelope<T>>(s_json, cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Ok != true || payload.Result is null)
        {
            throw new InvalidOperationException("Не удалось получить файл из Telegram.");
        }

        return payload;
    }

    private static string Limit(string value, int maximumLength) => value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record TelegramResponse(bool Ok);
    private sealed record TelegramResultEnvelope<T>(bool Ok, T? Result);
    private sealed record TelegramFileEnvelope([property: JsonPropertyName("file_path")] string? FilePath);
}
