namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат планирования email-рассылки. </summary>
public sealed class EmailCampaignScheduleResponse
{
    /// <summary> Идентификатор созданной кампании. </summary>
    public Guid CampaignId { get; init; }

    /// <summary> Количество запланированных отправок. </summary>
    public int ScheduledCount { get; init; }

    /// <summary> Интервал между отправками в секундах. </summary>
    public int IntervalSeconds { get; init; }

    /// <summary> Время первой отправки. </summary>
    public DateTime FirstSendAt { get; init; }

    /// <summary> Время последней отправки. </summary>
    public DateTime LastSendAt { get; init; }
}
