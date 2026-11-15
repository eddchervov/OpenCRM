namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запись о webhook-событии письма. </summary>
public sealed class EmailMessageEventItem
{
    /// <summary> Идентификатор события. </summary>
    public long Id { get; init; }

    /// <summary> Идентификатор письма, если сопоставлено. </summary>
    public int? EmailMessageId { get; init; }

    /// <summary> Имя провайдера, приславшего событие. </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary> Название события. </summary>
    public string EventName { get; init; } = string.Empty;

    /// <summary> Статус из события, если указан. </summary>
    public string? Status { get; init; }

    /// <summary> Email из события, если указан. </summary>
    public string? Email { get; init; }

    /// <summary> Идентификатор письма у провайдера, если известен. </summary>
    public string? ProviderMessageId { get; init; }

    /// <summary> Идентификатор кампании, если указан. </summary>
    public string? CampaignId { get; init; }

    /// <summary> URL перехода, если событие связано с кликом. </summary>
    public string? Url { get; init; }

    /// <summary> IP-адрес, если передан провайдером. </summary>
    public string? IpAddress { get; init; }

    /// <summary> User-Agent, если передан провайдером. </summary>
    public string? UserAgent { get; init; }

    /// <summary> Сведения о доставке из события. </summary>
    public string? DeliveryInfo { get; init; }

    /// <summary> Исходный payload события от провайдера. </summary>
    public string RawPayload { get; init; } = string.Empty;

    /// <summary> Признак успешной проверки подписи webhook. </summary>
    public bool SignatureVerified { get; init; }

    /// <summary> Время события от провайдера. </summary>
    public DateTime EventAt { get; init; }

    /// <summary> Время получения события системой. </summary>
    public DateTime ReceivedAt { get; init; }
}
