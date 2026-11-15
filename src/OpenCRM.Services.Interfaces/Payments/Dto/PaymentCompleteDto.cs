namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO состояния завершения платежа. </summary>
public class PaymentCompleteDto
{
    /// <summary> Признак подтверждения платежа. </summary>
    public bool IsConfirmed { get; set; }

    /// <summary> Признак отмены платежа. </summary>
    public bool IsCanceled { get; set; }

    /// <summary> Код статуса платежа. </summary>
    public string StatusCode { get; set; } = "returned";

    /// <summary> Основной URL действия после завершения платежа. </summary>
    public string PrimaryActionUrl { get; set; } = "/subscription";
}
