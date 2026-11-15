namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на планирование email-рассылки. </summary>
public sealed class EmailCampaignScheduleRequest : EmailCampaignRecipientsRequest
{
    /// <summary> Тема письма рассылки. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> HTML-тело письма рассылки. </summary>
    public string HtmlBody { get; init; } = string.Empty;

    /// <summary> Интервал между отправками в секундах. </summary>
    public int IntervalSeconds { get; init; } = 60;

    /// <summary> Задержка старта рассылки в минутах. </summary>
    public int StartDelayMinutes { get; init; }
}
