using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO ответа о платеже ЮKassa. </summary>
public class YooKassaPaymentResponse
{
    /// <summary> Идентификатор платежа. </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary> Статус платежа. </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary> Сумма платежа. </summary>
    [JsonPropertyName("amount")]
    public YooKassaAmount Amount { get; set; } = null!;

    /// <summary> Данные подтверждения, если есть. </summary>
    [JsonPropertyName("confirmation")]
    public YooKassaConfirmation? Confirmation { get; set; }

    /// <summary> Признак оплаты. </summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary> Дата создания платежа. </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary> Метаданные платежа, если есть. </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
