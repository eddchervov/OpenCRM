namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO покупателя платежа. </summary>
public sealed class PaymentBuyerDto
{
    /// <summary> Код типа покупателя. </summary>
    public string TypeCode { get; init; } = string.Empty;

    /// <summary> Название покупателя. </summary>
    public string? Name { get; init; }

    /// <summary> ИНН покупателя. </summary>
    public string? Inn { get; init; }
}
