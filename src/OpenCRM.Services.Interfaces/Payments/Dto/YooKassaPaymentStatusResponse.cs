using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO ответа о статусе платежа ЮKassa. </summary>
public class YooKassaPaymentStatusResponse
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

    /// <summary> Сумма возврата, если есть. </summary>
    [JsonPropertyName("refunded_amount")]
    public YooKassaAmount? RefundedAmount { get; set; }

    /// <summary> Признак оплаты. </summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary> Дата создания платежа. </summary>
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
