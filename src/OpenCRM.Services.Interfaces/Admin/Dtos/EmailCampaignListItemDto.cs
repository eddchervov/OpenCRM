using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO элемента списка email-кампаний. </summary>
public sealed class EmailCampaignListItemDto
{
    /// <summary> Идентификатор кампании. </summary>
    public Guid Id { get; init; }

    /// <summary> Тема писем кампании. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> Код статуса кампании. </summary>
    public string StatusCode { get; init; } = EmailCampaignStatusCodes.Scheduled;

    /// <summary> Режим выбора получателей. </summary>
    public string TargetMode { get; init; } = EmailCampaignTargetModeCodes.User;

    /// <summary> Код типа организации для выбора получателей. </summary>
    public string? TargetOrganizationTypeCode { get; init; }

    /// <summary> Интервал между отправками в секундах. </summary>
    public int IntervalSeconds { get; init; }

    /// <summary> Время старта кампании. </summary>
    public DateTime StartAt { get; init; }

    /// <summary> Дата создания кампании. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата завершения кампании. </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary> Общее количество писем кампании. </summary>
    public int TotalCount { get; init; }

    /// <summary> Количество отправленных писем кампании. </summary>
    public int SentCount { get; init; }

    /// <summary> Количество симулированных отправок в кампании. </summary>
    public int SimulatedCount { get; init; }

    /// <summary> Количество неудачных отправок. </summary>
    public int FailedCount { get; init; }

    /// <summary> Количество пропущенных получателей. </summary>
    public int SkippedCount { get; init; }

    /// <summary> Текст последней ошибки кампании. </summary>
    public string? LastError { get; init; }
}
