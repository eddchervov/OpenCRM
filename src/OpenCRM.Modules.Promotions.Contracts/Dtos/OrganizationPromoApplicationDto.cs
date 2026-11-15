namespace OpenCRM.Modules.Promotions.Contracts.Dtos;

/// <summary> DTO применения промокода организацией. </summary>
public class OrganizationPromoApplicationDto
{
    /// <summary> Идентификатор применения промокода. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Код промокода. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Название промокода. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Код типа выгоды промокода. </summary>
    public string BenefitTypeCode { get; set; } = string.Empty;

    /// <summary> Количество бонусных дней, если задано. </summary>
    public int? BonusDays { get; set; }

    /// <summary> Величина скидки, если задана. </summary>
    public decimal? DiscountValue { get; set; }

    /// <summary> Дата окончания действия скидки, если задана. </summary>
    public DateTime? DiscountValidUntil { get; set; }

    /// <summary> Статус применения промокода. </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary> Дата применения промокода. </summary>
    public DateTime AppliedAt { get; set; }

    /// <summary> Дата использования применения, если была. </summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Дата окончания доступа, если задана. </summary>
    public DateTime? AccessUntil { get; set; }
}
