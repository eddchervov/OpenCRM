using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO суммы платежа ЮKassa. </summary>
public class YooKassaAmount
{
    /// <summary> Строковое значение суммы. </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary> Валюта суммы. </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}
