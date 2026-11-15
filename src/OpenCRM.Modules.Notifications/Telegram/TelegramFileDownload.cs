namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Ограниченный поток Telegram-файла, владеющий HTTP-ответом relay.</summary>
public sealed class TelegramFileDownload(HttpResponseMessage response, Stream content) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public long SizeBytes { get; } = response.Content.Headers.ContentLength ?? 0;
    public string ContentType { get; } = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        response.Dispose();

        return ValueTask.CompletedTask;
    }
}
