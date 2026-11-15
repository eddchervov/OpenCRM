using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO уведомления вебхука ЮKassa. </summary>
public class YooKassaNotification
{
    /// <summary> Тип уведомления. </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary> Событие уведомления. </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    /// <summary> Объект платежа в уведомлении. </summary>
    [JsonPropertyName("object")]
    public YooKassaPayment Object { get; set; } = null!;
}
