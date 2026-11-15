namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> Расчёт цены с учётом промокода. </summary>
public class PromotionPriceDto
{
    /// <summary> Исходная цена без скидки. </summary>
    public decimal OriginalPrice { get; set; }

    /// <summary> Итоговая цена после скидки. </summary>
    public decimal FinalPrice { get; set; }

    /// <summary> Сумма скидки. </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary> Идентификатор промокода, если применён. </summary>
    public int? PromoCodeId { get; set; }

    /// <summary> Код применённого промокода. </summary>
    public string? PromoCode { get; set; }

    /// <summary> Идентификатор организации, если применим. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Идентификатор применения промокода. </summary>
    public long? PromoCodeApplicationId { get; set; }

    /// <summary> Признак наличия скидки. </summary>
    public bool HasDiscount => PromoCodeId.HasValue && DiscountAmount > 0;
}
