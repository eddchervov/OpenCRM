namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> DTO применения промокода в платеже. </summary>
public class PaymentPromotionDto
{
    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Код промокода. </summary>
    public string PromoCode { get; set; } = string.Empty;

    /// <summary> Сумма до скидки. </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary> Сумма скидки. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Идентификатор организации, если применимо. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Идентификатор применения промокода, если есть. </summary>
    public long? PromoCodeApplicationId { get; set; }
}
