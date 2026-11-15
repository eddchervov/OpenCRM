namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Сводные показатели по письмам за период. </summary>
public sealed class EmailDashboardResponse
{
    /// <summary> Начало периода сводки. </summary>
    public DateTime Since { get; init; }

    /// <summary> Общее количество сообщений за период. </summary>
    public int TotalMessages { get; init; }

    /// <summary> Общее количество webhook-событий за период. </summary>
    public int TotalWebhookEvents { get; init; }

    /// <summary> Время последнего webhook-события, если было. </summary>
    public DateTime? LastWebhookAt { get; init; }

    /// <summary> Распределение сообщений по статусам. </summary>
    public IReadOnlyList<EmailMetricItem> MessageStatusCounts { get; init; } = [];

    /// <summary> Распределение событий по статусам. </summary>
    public IReadOnlyList<EmailMetricItem> EventStatusCounts { get; init; } = [];
}
