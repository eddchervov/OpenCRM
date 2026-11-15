namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос тестовой отправки письма. </summary>
public sealed class EmailTestSendRequest
{
    /// <summary> Email адресата тестового письма. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Тема тестового письма, если переопределена. </summary>
    public string? Subject { get; init; }

    /// <summary> HTML-тело тестового письма, если переопределено. </summary>
    public string? HtmlBody { get; init; }
}
