using System.Text.Json.Serialization;

namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO подтверждения платежа ЮKassa. </summary>
public class YooKassaConfirmation
{
    /// <summary> Тип подтверждения. </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary> URL для подтверждения платежа. </summary>
    [JsonPropertyName("confirmation_url")]
    public string ConfirmationUrl { get; set; } = string.Empty;
}
