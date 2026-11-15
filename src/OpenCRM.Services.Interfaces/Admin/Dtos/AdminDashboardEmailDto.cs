namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO письма для дашборда. </summary>
public sealed class AdminDashboardEmailDto
{
    /// <summary> Идентификатор письма. </summary>
    public int Id { get; init; }

    /// <summary> Email получателя. </summary>
    public string ToEmail { get; init; } = string.Empty;

    /// <summary> Тема письма. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> Код статуса письма. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Время создания письма. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время отправки письма. </summary>
    public DateTime? SentAt { get; init; }

    /// <summary> Связанный пользователь. </summary>
    public string? User { get; init; }
}

