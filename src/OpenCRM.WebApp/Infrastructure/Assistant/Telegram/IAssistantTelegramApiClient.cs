using System.Text.Json.Serialization;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

public sealed record AssistantTelegramButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData);

public interface IAssistantTelegramApiClient
{
    Task SendMessageAsync(
        long chatId,
        string text,
        IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>? buttons,
        CancellationToken cancellationToken);

    Task SendVoiceAsync(long chatId, byte[] content, string contentType, CancellationToken cancellationToken);

    Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken cancellationToken);

    Task<(byte[] Content, string ContentType)> DownloadVoiceAsync(string fileId, long maximumBytes, CancellationToken cancellationToken);
}
