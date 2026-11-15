using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат создания email-кампании. </summary>
public sealed class EmailCampaignCreateResultDto
{
    /// <summary> Идентификатор кампании. </summary>
    public Guid CampaignId { get; init; }

    /// <summary> Количество запланированных писем. </summary>
    public int ScheduledCount { get; init; }

    /// <summary> Интервал между отправками в секундах. </summary>
    public int IntervalSeconds { get; init; }

    /// <summary> Время первой отправки. </summary>
    public DateTime FirstSendAt { get; init; }

    /// <summary> Время последней отправки. </summary>
    public DateTime LastSendAt { get; init; }
}

