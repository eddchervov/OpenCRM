namespace OpenCRM.Admin.Api.Models;

/// <summary> Письмо для административной панели. </summary>
public sealed class AdminDashboardEmail
{
    /// <summary> Идентификатор письма. </summary>
    public int Id { get; init; }

    /// <summary> Email получателя. </summary>
    public string ToEmail { get; init; } = string.Empty;

    /// <summary> Тема письма. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> Код статуса письма. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Момент создания письма. </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary> Момент отправки письма. </summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary> Имя пользователя-получателя. </summary>
    public string? User { get; init; }
}
