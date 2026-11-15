using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO создания email-кампании. </summary>
public sealed class EmailCampaignCreateDto
{
    /// <summary> Тема писем кампании. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> HTML-тело писем кампании. </summary>
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary> Режим выбора получателей. </summary>
    public string TargetMode { get; init; } = EmailCampaignTargetModeCodes.User;

    /// <summary> Идентификатор пользователя-получателя. </summary>
    public string? TargetUserId { get; init; }

    /// <summary> Код типа организации для выбора получателей. </summary>
    public string? TargetOrganizationTypeCode { get; init; }

    /// <summary> Признак отправки только пользователям с подтверждённым email. </summary>
    public bool OnlyConfirmedEmail { get; init; } = true;

    /// <summary> Признак отправки только пользователям с согласием на рекламу. </summary>
    public bool OnlyAdvertisingConsent { get; init; }

    /// <summary> Интервал между отправками писем в секундах. </summary>
    public int IntervalSeconds { get; init; } = 60;

    /// <summary> Задержка старта кампании в минутах. </summary>
    public int StartDelayMinutes { get; init; }

    /// <summary> Список получателей кампании. </summary>
    public IReadOnlyList<EmailCampaignRecipientCreateDto> Recipients { get; init; } = [];
}

