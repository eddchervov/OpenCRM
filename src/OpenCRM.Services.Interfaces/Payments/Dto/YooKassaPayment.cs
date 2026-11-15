using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> Модель платежа ЮKassa. </summary>
public class YooKassaPayment
{
    /// <summary> Идентификатор платежа в ЮKassa. </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary> Статус платежа в ЮKassa. </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary> Признак оплаты в ЮKassa. </summary>
    [JsonPropertyName("paid")]
    public bool Paid { get; set; }

    /// <summary> Сумма платежа. </summary>
    [JsonPropertyName("amount")]
    public YooKassaAmount Amount { get; set; } = null!;

    /// <summary> Метаданные платежа. </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
